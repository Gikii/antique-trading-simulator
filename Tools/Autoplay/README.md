# Autoplay

Plays the campaign without a person, so economy changes can be tested and tuned on the
diagnostics CSVs instead of by hand.

A run starts a new game, steps the clock tick by tick as fast as the machine allows (no
real-time waiting), turns down every auction, and lets a bot act for the player once per
in-game day. Same seed + same arguments = the same game (`--repro` checks it), so two runs
that differ in one parameter can be compared directly.

## 1. Build the player (once per code change)

Unity: **Tools → Antique Trading Simulator → Autoplay → Build autoplay player**.
It builds the current desktop platform into `Builds/Autoplay/` with `ATS_DIAGNOSTICS`
defined (all 34 tables, seeding). Player Settings are not touched.

## 2. Run the experiments

```bash
python Tools/Autoplay/run_experiments.py --only quick --workers 1   # smoke test, ~1 min
python Tools/Autoplay/run_experiments.py --repro --only idle        # same seed twice, CSVs must match
python Tools/Autoplay/run_experiments.py --workers 3 --zip          # everything in experiments.json
```

Results go to `AutoplayResults/<date-time>/` (git-ignored): `runs.csv` with one row per run,
then `<experiment>/seed<N>/` with `player.log` and the `session_*` folder of CSVs plus
`autoplay_summary.json` and `autoplay_actions.csv` (everything the bot did).
`--zip` packs it without the heavy per-decision tables (`--zip-all` keeps them).

Other options: `--list`, `--seeds 1,2`, `--weeks 30`, `--timeout 90`, `--graphics`, `--game <exe>`.

## 3. Watch one run in the editor

**Tools → Antique Trading Simulator → Autoplay**, pick a preset or type arguments, **Play**.
CSVs go to the usual diagnostics folder (`Application.persistentDataPath/diagnostics`).

## Strategies

| `-autoplayStrategy:` | What the bot does |
|---|---|
| `idle` | nothing — the market as the NPCs make it (control run) |
| `buyup` | from `-autoplayFrom` to `-autoplayTo` buys every listing of `-autoplayCategory` it can afford, cheapest first; from `-autoplaySell` dumps them (instant sales). `-autoplayGrant:<£>` gives it money when buying starts |
| `trader` | buys pieces priced below worth (price factor ≤ 0.85), lists them at worth +5%, sells at once what hasn't sold in 8 weeks |

Common: `-autoplayWeeks:<n>` (stop early), `-autoplayReserve:<£>` (cash never spent, default 2),
`-autoplayTicksPerFrame:<n>` (default 28 = a week per frame).

## Parameter overrides

`-set:<target>.<member>=<value>`, repeatable, applied before the game starts:

```text
-set:speculator.TrendSensitivity=0.8        NPC profile, by id / ProfileName / asset name
-set:EconomyManager.longTermDemandCap=0     component in the game scene (private [SerializeField] too)
-set:Market.OwnTrendWeight=0.3              static field or property
```

Profiles are changed on in-memory copies, never on the assets. A component field only
matters if the game reads it after the scene loaded. A misspelt override stops the run
with an error instead of silently running the wrong experiment. Every override applied
is listed in `autoplay_summary.json`.

## Experiments (`experiments.json`)

Each entry has `name`, `args`, optional `seeds`, `weeks`, `description`, and `extends`
(inherit another entry's arguments and add more). `manual: true` entries run only with `--only`.
