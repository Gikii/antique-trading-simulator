using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// One append-only CSV file. The column count comes from the header and every row is checked
    /// against it, so a mismatch throws on the first write instead of silently shifting columns.
    /// Buffered: flushed once a week and on shutdown.
    /// </summary>
    public sealed class CsvTable : IDisposable
    {
        public string Name { get; }
        public int ColumnCount { get; }
        public long RowsWritten { get; private set; }
        public long RowsRejected { get; private set; }

        private StreamWriter _writer;
        private readonly CsvRow _pooled;
        private bool _pooledInUse;

        public CsvTable(string directory, string name, string header)
        {
            Name = name;
            ColumnCount = 1;
            for (int i = 0; i < header.Length; i++)
                if (header[i] == ',') ColumnCount++;

            _writer = new StreamWriter(Path.Combine(directory, name + ".csv"), false, new UTF8Encoding(false))
            {
                AutoFlush = false
            };
            _writer.Write(header);
            _writer.Write('\n');
            _pooled = new CsvRow(this);
        }

        private CsvTable(string name)
        {
            Name = name;
            ColumnCount = 0;
            _writer = null;
            _pooled = new CsvRow(this);
        }

        /// <summary>
        /// A table that accepts rows and drops them, with no file behind it. Handed out once a
        /// session has closed, so a stray event from a subscription that outlived it writes
        /// nowhere instead of opening a new file.
        /// </summary>
        public static CsvTable Discarding() => new CsvTable("(discarded)");

        /// <summary>Starts a row. Re-entrant: a nested row gets its own buffer rather than sharing.</summary>
        public CsvRow Open()
        {
            if (_pooledInUse) return new CsvRow(this).Begin();

            _pooledInUse = true;
            return _pooled.Begin();
        }

        internal void Commit(CsvRow row, List<string> cells)
        {
            if (ReferenceEquals(row, _pooled)) _pooledInUse = false;
            if (_writer == null) return;

            if (cells.Count != ColumnCount)
            {
                RowsRejected++;
                throw new InvalidOperationException(
                    $"CsvTable '{Name}': row has {cells.Count} cells, header has {ColumnCount}.");
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) _writer.Write(',');
                _writer.Write(cells[i]);
            }
            _writer.Write('\n');
            RowsWritten++;
        }

        internal void Abandon(CsvRow row)
        {
            if (ReferenceEquals(row, _pooled)) _pooledInUse = false;
        }

        public void Flush() => _writer?.Flush();

        public void Dispose()
        {
            if (_writer == null) return;

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            finally
            {
                _writer = null;
            }
        }

        // ---------------------------------------------------------------- value formatting

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Text(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needsQuotes = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ',' || c == '"' || c == '\n' || c == '\r') { needsQuotes = true; break; }
            }

            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "";
            return value.ToString("0.######", Inv);
        }

        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";
            return value.ToString("0.######", Inv);
        }

        public static string Whole(long value) => value.ToString(Inv);

        public static string Flag(bool value) => value ? "true" : "false";

        public static string Day(DateTime value) => value.ToString("yyyy-MM-dd", Inv);
    }

    /// <summary>
    /// Builds one row. Short method names keep the recorders readable: S text, I whole, F number,
    /// B flag, E enum name, D date, N empty.
    /// </summary>
    public sealed class CsvRow
    {
        private readonly CsvTable _table;
        private readonly List<string> _cells = new List<string>(64);

        internal CsvRow(CsvTable table) => _table = table;

        internal CsvRow Begin()
        {
            _cells.Clear();
            return this;
        }

        public int Count => _cells.Count;

        public CsvRow S(string value) { _cells.Add(CsvTable.Text(value)); return this; }

        // int has its own overload so a plain int argument is an exact match: without it the call
        // would be ambiguous between long and int?.
        public CsvRow I(int value) { _cells.Add(CsvTable.Whole(value)); return this; }
        public CsvRow I(long value) { _cells.Add(CsvTable.Whole(value)); return this; }
        public CsvRow I(int? value) { _cells.Add(value.HasValue ? CsvTable.Whole(value.Value) : ""); return this; }
        public CsvRow F(float value) { _cells.Add(CsvTable.Number(value)); return this; }
        public CsvRow F(float? value) { _cells.Add(value.HasValue ? CsvTable.Number(value.Value) : ""); return this; }
        public CsvRow F(double value) { _cells.Add(CsvTable.Number(value)); return this; }
        public CsvRow B(bool value) { _cells.Add(CsvTable.Flag(value)); return this; }
        public CsvRow B(bool? value) { _cells.Add(value.HasValue ? CsvTable.Flag(value.Value) : ""); return this; }
        public CsvRow E(Enum value) { _cells.Add(value != null ? value.ToString() : ""); return this; }
        public CsvRow D(DateTime value) { _cells.Add(CsvTable.Day(value)); return this; }
        public CsvRow N() { _cells.Add(""); return this; }

        /// <summary>Adds the same number of empty cells, for columns a row cannot fill.</summary>
        public CsvRow N(int count)
        {
            for (int i = 0; i < count; i++) _cells.Add("");
            return this;
        }

        public void End() => _table.Commit(this, _cells);

        /// <summary>Drops the row without writing it, releasing the pooled buffer.</summary>
        public void Cancel() => _table.Abandon(this);
    }
}
