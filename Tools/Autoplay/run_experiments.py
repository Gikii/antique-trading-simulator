#!/usr/bin/env python3
"""Run batches of autoplay games and collect their diagnostics CSVs.

Starts the standalone build made by Tools -> Antique Trading Simulator -> Autoplay ->
"Build autoplay player" once per (experiment, seed), headless and in parallel, and writes:

    AutoplayResults/<stamp>/
        runs.csv                         one row per run: status, exit code, time, final week
        <experiment>/seed<N>/
            player.log                   Unity's log of that run
            session_01_.../*.csv         the diagnostics tables
            session_01_.../autoplay_summary.json
            session_01_.../autoplay_actions.csv

Examples:
    python Tools/Autoplay/run_experiments.py --list
    python Tools/Autoplay/run_experiments.py --only quick --workers 1
    python Tools/Autoplay/run_experiments.py --repro
    python Tools/Autoplay/run_experiments.py --workers 3 --zip

Standard library only (Python 3.8+).
"""
from __future__ import annotations

import argparse
import concurrent.futures as futures
import csv
import datetime as dt
import json
import os
import platform
import subprocess
import sys
import time
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
DEFAULT_PLAN = HERE / "experiments.json"
DEFAULT_BUILD_DIR = REPO / "Builds" / "Autoplay"
DEFAULT_RESULTS = REPO / "AutoplayResults"

# Tables whose content depends on the wall clock, so they differ between identical runs.
NON_DETERMINISTIC_TABLES = {"dim_run", "fact_session", "fact_recorder_error"}
# Big per-decision tables left out of --zip unless --zip-all (they can reach hundreds of MB).
HEAVY_TABLES = {"fact_browse_funnel", "fact_npc_action", "fact_action_gate", "fact_news_reception"}


# ---------------------------------------------------------------------------- plan

def load_plan(path: Path) -> dict:
    with path.open(encoding="utf-8") as f:
        plan = json.load(f)
    defaults = plan.get("defaults", {})
    by_name = {e["name"]: e for e in plan["experiments"]}

    def resolve(exp: dict, seen: tuple = ()) -> dict:
        if exp["name"] in seen:
            raise SystemExit(f"experiments.json: circular 'extends' at {exp['name']}")
        base = {}
        if "extends" in exp:
            parent = by_name.get(exp["extends"])
            if parent is None:
                raise SystemExit(f"experiments.json: {exp['name']} extends unknown '{exp['extends']}'")
            base = resolve(parent, seen + (exp["name"],))
        # "manual" and "description" describe the entry itself, so they are not inherited.
        inherited = {k: v for k, v in base.items() if k not in ("name", "manual", "description")}
        merged = {**defaults, **inherited, **exp}
        merged["args"] = list(base.get("args", [])) + list(exp.get("args", []))
        return merged

    return {"defaults": defaults, "experiments": [resolve(e) for e in plan["experiments"]]}


# ---------------------------------------------------------------------------- game

def find_game(explicit: str | None) -> Path:
    if explicit:
        path = Path(explicit)
    else:
        candidates = [DEFAULT_BUILD_DIR / n for n in
                      ("AntiqueTradingSimulator.exe", "AntiqueTradingSimulator.x86_64", "AntiqueTradingSimulator.app")]
        path = next((c for c in candidates if c.exists()), candidates[0])
    if path.suffix == ".app":  # macOS bundle: run the binary inside it
        inner = list((path / "Contents" / "MacOS").glob("*"))
        if inner:
            path = inner[0]
    if not path.exists():
        raise SystemExit(f"No game build at {path}.\n"
                         "Build it in Unity: Tools -> Antique Trading Simulator -> Autoplay -> Build autoplay player,\n"
                         "or pass --game <path to the executable>.")
    return path


def command(game: Path, run_dir: Path, seed: int, weeks: int | None, args: list[str], graphics: bool) -> list[str]:
    cmd = [str(game)]
    if not graphics:
        cmd += ["-batchmode", "-nographics"]
    cmd += ["-logFile", str(run_dir / "player.log"),
            "-autoplay", "-autoplayQuit", "-atsDiagnostics",
            f"-atsSeed:{seed}", f"-atsOutput:{run_dir}"]
    if weeks:
        cmd.append(f"-autoplayWeeks:{weeks}")
    return cmd + list(args)


def run_one(game: Path, out: Path, exp: dict, seed: int, timeout_min: float, graphics: bool) -> dict:
    run_dir = out / exp["name"] / f"seed{seed}"
    run_dir.mkdir(parents=True, exist_ok=True)
    cmd = command(game, run_dir, seed, exp.get("weeks"), exp["args"], graphics)
    (run_dir / "command.txt").write_text(" ".join(f'"{c}"' if " " in c else c for c in cmd) + "\n", encoding="utf-8")

    started = time.time()
    status, code = "ok", None
    try:
        proc = subprocess.run(cmd, cwd=run_dir, timeout=timeout_min * 60,
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        code = proc.returncode
    except subprocess.TimeoutExpired:
        status = "timeout"
    seconds = time.time() - started

    summary_path = next(run_dir.rglob("autoplay_summary.json"), None)
    summary = {}
    if summary_path:
        try:
            summary = json.loads(summary_path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            summary = {}
    if status == "ok" and (not summary or summary.get("exit_code", 1) != 0):
        status = "failed"

    sessions = sorted(p for p in run_dir.glob("session_*") if p.is_dir())
    return {
        "experiment": exp["name"], "seed": seed, "status": status, "exit_code": code,
        "seconds": round(seconds, 1), "week": summary.get("week", ""), "reason": summary.get("reason", "no summary"),
        "player_wealth": summary.get("player_wealth", ""),
        "session_dir": str(sessions[-1].relative_to(out)) if sessions else "",
    }


# ---------------------------------------------------------------------------- reproducibility

def compare_runs(a: Path, b: Path) -> list[str]:
    """Differences between two runs' CSVs, ignoring run ids and wall-clock tables."""
    sa = next((p for p in sorted(a.glob("session_*")) if p.is_dir()), None)
    sb = next((p for p in sorted(b.glob("session_*")) if p.is_dir()), None)
    if not sa or not sb:
        return ["one of the runs has no session folder"]

    problems = []
    for file_a in sorted(sa.glob("*.csv")):
        name = file_a.stem
        if name in NON_DETERMINISTIC_TABLES:
            continue
        file_b = sb / file_a.name
        if not file_b.exists():
            problems.append(f"{name}: missing in the second run")
            continue
        with file_a.open(newline="", encoding="utf-8") as fa, file_b.open(newline="", encoding="utf-8") as fb:
            ra, rb = csv.reader(fa), csv.reader(fb)
            header = next(ra, [])
            next(rb, None)
            drop = {i for i, col in enumerate(header) if col == "run_id"}
            line = 1
            while True:
                row_a, row_b = next(ra, None), next(rb, None)
                line += 1
                if row_a is None and row_b is None:
                    break
                if row_a is None or row_b is None:
                    problems.append(f"{name}: different number of rows (from line {line})")
                    break
                clean_a = [v for i, v in enumerate(row_a) if i not in drop]
                clean_b = [v for i, v in enumerate(row_b) if i not in drop]
                if clean_a != clean_b:
                    diff = [header[i] for i in range(len(row_a))
                            if i not in drop and i < len(row_b) and row_a[i] != row_b[i]]
                    problems.append(f"{name}: first difference on line {line} (columns: {', '.join(diff[:6])})")
                    break
    return problems


# ---------------------------------------------------------------------------- zip

def make_zip(out: Path, include_all: bool) -> Path:
    target = out.with_suffix(".zip")
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for path in sorted(out.rglob("*")):
            if not path.is_file():
                continue
            if path.suffix == ".csv" and path.stem in HEAVY_TABLES and not include_all:
                continue
            if path.name == "player.log" and path.stat().st_size > 5_000_000:
                continue
            z.write(path, path.relative_to(out.parent))
    return target


# ---------------------------------------------------------------------------- main

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="path to the autoplay build (default: Builds/Autoplay/...)")
    ap.add_argument("--plan", default=str(DEFAULT_PLAN), help="experiments file (default: experiments.json here)")
    ap.add_argument("--only", help="comma-separated experiment names to run")
    ap.add_argument("--seeds", help="comma-separated seeds, overriding the plan")
    ap.add_argument("--weeks", type=int, help="stop every run after this week (quick tests)")
    ap.add_argument("--workers", type=int, default=max(1, min(3, (os.cpu_count() or 2) // 2)),
                    help="games run at once (each takes one CPU core and ~1-2 GB RAM)")
    ap.add_argument("--timeout", type=float, default=60, help="minutes before a run is killed (default 60)")
    ap.add_argument("--out", help="results folder (default: AutoplayResults/<date-time>)")
    ap.add_argument("--graphics", action="store_true", help="show the game windows instead of running headless")
    ap.add_argument("--repro", action="store_true",
                    help="run the first selected experiment twice with the same seed and check the CSVs match")
    ap.add_argument("--zip", action="store_true", help="zip the results to send (without the heavy per-decision tables)")
    ap.add_argument("--zip-all", action="store_true", help="like --zip but with every table")
    ap.add_argument("--list", action="store_true", help="list the experiments and exit")
    a = ap.parse_args()

    plan = load_plan(Path(a.plan))
    experiments = plan["experiments"]
    if a.only:
        wanted = [n.strip() for n in a.only.split(",") if n.strip()]
        unknown = [n for n in wanted if n not in {e["name"] for e in experiments}]
        if unknown:
            raise SystemExit(f"Unknown experiment(s): {', '.join(unknown)}. Use --list.")
        experiments = [e for e in experiments if e["name"] in wanted]
    else:
        # "manual" experiments (smoke tests) only run when asked for by name.
        experiments = [e for e in experiments if not e.get("manual")]
    if a.weeks:
        for e in experiments:
            e["weeks"] = a.weeks
    seeds_override = [int(s) for s in a.seeds.split(",")] if a.seeds else None

    if a.list:
        for e in experiments:
            seeds = seeds_override or e.get("seeds", [1])
            print(f"{e['name']:<24} seeds {seeds}  {e.get('description', '')}\n    {' '.join(e['args'])}")
        return 0

    game = find_game(a.game)
    out = Path(a.out) if a.out else DEFAULT_RESULTS / dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True, exist_ok=True)

    if a.repro:
        exp = dict(experiments[0])
        seed = (seeds_override or exp.get("seeds", [1]))[0]
        print(f"Reproducibility check: '{exp['name']}', seed {seed}, twice …")
        results = []
        for label in ("a", "b"):
            copy = {**exp, "name": f"_repro_{exp['name']}_{label}"}
            results.append(run_one(game, out, copy, seed, a.timeout, a.graphics))
            print(f"  run {label}: {results[-1]['status']} ({results[-1]['seconds']} s)")
        if any(r["status"] != "ok" for r in results):
            print("A run failed — see player.log in", out)
            return 1
        problems = compare_runs(out / f"_repro_{exp['name']}_a" / f"seed{seed}",
                                out / f"_repro_{exp['name']}_b" / f"seed{seed}")
        if problems:
            print("NOT reproducible:")
            for p in problems:
                print("  -", p)
            return 2
        print("Reproducible: every deterministic table is identical.")
        return 0

    jobs = [(e, s) for e in experiments for s in (seeds_override or e.get("seeds", [1]))]
    print(f"{len(jobs)} runs, {a.workers} at a time -> {out}\nGame: {game}\n")

    (out / "plan_used.json").write_text(json.dumps(
        {"argv": sys.argv, "platform": platform.platform(), "experiments": experiments}, indent=2), encoding="utf-8")

    rows = []
    started = time.time()
    with futures.ThreadPoolExecutor(max_workers=a.workers) as pool:
        pending = {pool.submit(run_one, game, out, e, s, a.timeout, a.graphics): (e["name"], s) for e, s in jobs}
        for i, done in enumerate(futures.as_completed(pending), 1):
            name, seed = pending[done]
            try:
                row = done.result()
            except Exception as exc:  # noqa: BLE001 - report and carry on with the other runs
                row = {"experiment": name, "seed": seed, "status": "crashed", "reason": repr(exc)}
            rows.append(row)
            print(f"[{i}/{len(jobs)}] {name} seed {seed}: {row['status']}"
                  f"{'' if row['status'] == 'ok' else ' — ' + str(row.get('reason', ''))}"
                  f" ({row.get('seconds', '?')} s)")

    rows.sort(key=lambda r: (r["experiment"], r["seed"]))
    fields = ["experiment", "seed", "status", "exit_code", "seconds", "week", "reason", "player_wealth", "session_dir"]
    with (out / "runs.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)

    failed = [r for r in rows if r["status"] != "ok"]
    print(f"\nDone in {(time.time() - started) / 60:.1f} min: {len(rows) - len(failed)} ok, {len(failed)} not."
          f"\nSummary: {out / 'runs.csv'}")

    if a.zip or a.zip_all:
        archive = make_zip(out, include_all=a.zip_all)
        print(f"Zipped: {archive} ({archive.stat().st_size / 1e6:.1f} MB)")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
