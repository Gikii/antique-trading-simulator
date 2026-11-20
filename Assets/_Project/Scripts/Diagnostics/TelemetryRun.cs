using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// One recording session: its own output folder, its own set of tables, its own sequence
    /// counter. A session runs from the game scene loading — startup, a new game or a save being
    /// loaded — to the campaign reaching its score screen, the game quitting, or the scene being
    /// unloaded. A new session never writes into an older session's folder.
    ///
    /// Also wraps each hook so a recorder fault is written to fact_recorder_error instead of
    /// propagating into game code, and refuses all further work once closed, so a late event from
    /// a subscription that outlived the session cannot reopen a file.
    /// </summary>
    public sealed class TelemetryRun : IDisposable
    {
        public const string SchemaVersion = "1.1.0";

        public string RunId { get; }
        public string Directory { get; }
        public DateTime StartedUtc { get; }
        public int SessionIndex { get; }
        public bool Closed { get; private set; }

        public Core.TimeManager Clock { get; set; }

        private readonly Dictionary<string, CsvTable> _tables = new Dictionary<string, CsvTable>(T.TableCount);
        private readonly Dictionary<string, int> _errorCounts = new Dictionary<string, int>();
        private readonly CsvTable _discard = CsvTable.Discarding();
        private long _seq;

        public TelemetryRun(string root, int sessionIndex)
        {
            RunId = Guid.NewGuid().ToString("N");
            StartedUtc = DateTime.UtcNow;
            SessionIndex = sessionIndex;
            Directory = MakeFolder(root, sessionIndex, RunId);
        }

        /// <summary>
        /// A folder name no other session can take: the session's number in this process, the
        /// local time to the second, and the first eight characters of the run id. If that exists
        /// anyway, a counter is appended until it does not.
        /// </summary>
        private static string MakeFolder(string root, int sessionIndex, string runId)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string baseName = $"session_{sessionIndex:00}_{stamp}_{runId.Substring(0, 8)}";

            string path = Path.Combine(root, baseName);
            int suffix = 2;
            while (System.IO.Directory.Exists(path))
            {
                path = Path.Combine(root, baseName + "-" + suffix);
                suffix++;
            }

            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        // ---------------------------------------------------------------- tables

        public CsvTable Table(string name, string header)
        {
            if (_tables.TryGetValue(name, out var existing)) return existing;
            if (Closed) return _discard;

            try
            {
                var table = new CsvTable(Directory, name, header);
                _tables[name] = table;
                return table;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Telemetry: could not open {name}.csv — {e.GetType().Name}: {e.Message}");
                _tables[name] = _discard;
                return _discard;
            }
        }

        public void FlushAll()
        {
            if (Closed) return;
            foreach (var pair in _tables) pair.Value.Flush();
        }

        public long TotalRowsWritten
        {
            get
            {
                long rows = 0;
                foreach (var pair in _tables) rows += pair.Value.RowsWritten;
                return rows;
            }
        }

        /// <summary>
        /// Closes the files. The table objects are kept, not removed, so a stray event arriving
        /// afterwards writes into a closed table — a no-op — rather than creating a new file.
        /// </summary>
        public void Dispose()
        {
            if (Closed) return;
            Closed = true;

            foreach (var pair in _tables) pair.Value.Dispose();
        }

        public IEnumerable<KeyValuePair<string, CsvTable>> Tables => _tables;

        // ---------------------------------------------------------------- clock

        public long NextSeq() => ++_seq;
        public long CurrentSeq => _seq;

        private bool HasClock => Clock != null;

        public int Week => HasClock ? Clock.CurrentWeek : 0;
        public int Tick => HasClock ? Clock.CurrentTick : 0;
        public int TicksPerDay => HasClock ? Clock.TicksPerDay : Core.TimeManager.DefaultTicksPerDay;
        public int DayIndex => Tick / Mathf.Max(1, TicksPerDay);

        public DateTime Date => HasClock ? Clock.TickToDate(Clock.CurrentWeek, Clock.CurrentTick) : DateTime.MinValue;
        public DateTime WeekDate => HasClock ? Clock.WeekToDate(Clock.CurrentWeek) : DateTime.MinValue;

        /// <summary>run_id, seq, week, tick, day_index, game_date — the stamp on most fact tables.</summary>
        public CsvRow Stamp(CsvTable table) =>
            table.Open().S(RunId).I(NextSeq()).I(Week).I(Tick).I(DayIndex).D(Date);

        /// <summary>run_id, seq, week, game_date — the stamp on the weekly tables.</summary>
        public CsvRow StampWeek(CsvTable table) =>
            table.Open().S(RunId).I(NextSeq()).I(Week).D(WeekDate);

        /// <summary>run_id, seq, week, tick — fact_integrity and fact_recorder_error.</summary>
        public CsvRow StampShort(CsvTable table) =>
            table.Open().S(RunId).I(NextSeq()).I(Week).I(Tick);

        /// <summary>run_id only — the dimension tables.</summary>
        public CsvRow StampRun(CsvTable table) => table.Open().S(RunId);

        // ---------------------------------------------------------------- fault isolation

        /// <summary>
        /// Runs a hook. A throw is recorded and swallowed: a recorder bug must never be able to
        /// break the simulation. Repeat faults from the same hook are counted, not re-logged.
        /// </summary>
        public void Safe(string table, string hook, Action body)
        {
            if (Closed) return;

            try
            {
                body();
            }
            catch (Exception e)
            {
                RecordFault(table, hook, e);
            }
        }

        private void RecordFault(string table, string hook, Exception e)
        {
            string key = table + "|" + hook;
            _errorCounts.TryGetValue(key, out int count);
            count++;
            _errorCounts[key] = count;

            if (count == 1)
                Debug.LogWarning($"Telemetry: '{hook}' threw while writing {table} — {e.GetType().Name}: {e.Message}. " +
                                 "The game is unaffected; see fact_recorder_error.csv.");

            try
            {
                StampShort(Table(T.FactRecorderErrorName, T.FactRecorderErrorHeader))
                    .S(table)
                    .S(hook)
                    .S(e.GetType().Name)
                    .S(e.Message)
                    .I(count)
                    .I(1)
                    .End();
            }
            catch
            {
                // The error table itself is unavailable; nothing further to do.
            }
        }

        // ---------------------------------------------------------------- reflection

        /// <summary>
        /// Reads a private serialized field. Used only for dim_run, so the managers' tuning values
        /// can be captured without widening their API.
        /// </summary>
        public static T Field<T>(object target, string name, T fallback = default)
        {
            if (target == null) return fallback;

            try
            {
                var type = target.GetType();
                while (type != null)
                {
                    var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && typeof(T).IsAssignableFrom(field.FieldType))
                        return (T)field.GetValue(target);
                    type = type.BaseType;
                }
            }
            catch
            {
                return fallback;
            }

            return fallback;
        }

        public static string Join<TItem>(IEnumerable<TItem> items, Func<TItem, string> select)
        {
            if (items == null) return "";

            var sb = new System.Text.StringBuilder();
            foreach (var item in items)
            {
                string part = select(item);
                if (string.IsNullOrEmpty(part)) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(part);
            }
            return sb.ToString();
        }
    }
}
