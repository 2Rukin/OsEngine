/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace OsEngine.OsData.OrderFlow.Calibration
{
    internal sealed record CalibrationTableRow(IReadOnlyDictionary<string, object> Values, object Payload);
    internal sealed record TableCursor(object Value, long Ordinal);
    internal sealed record TableQuery(string Text = "", string TimeRange = "", string Direction = "Any", string NumericColumn = "",
        decimal? Minimum = null, decimal? Maximum = null, string SortColumn = "", int SortDirection = 0, TableCursor After = null);
    internal sealed record TablePage(IReadOnlyList<CalibrationTableRow> Rows, long Visible, long Total, TableCursor Next);

    /// <summary>Streaming keyset pagination; at most pageSize+1 rows survive sorting regardless of input length or page number.</summary>
    /// <remarks>Sort/filter runs on a worker and never changes models or semantic identity. Source order breaks equal-key ties.</remarks>
    internal sealed class CalibrationTableSource
    {
        internal string Title { get; }
        internal IReadOnlyList<string> Columns { get; }
        private readonly Func<CancellationToken, IEnumerable<CalibrationTableRow>> _read;
        internal CalibrationTableSource(string title, IEnumerable<string> columns, Func<CancellationToken, IEnumerable<CalibrationTableRow>> read)
        { Title = title; Columns = columns.ToArray(); _read = read; }

        internal static CalibrationTableSource Create<T>(string title, Func<CancellationToken, IEnumerable<T>> read, Func<T, object> payload = null)
        {
            PropertyInfo[] properties = typeof(T).GetProperties().Where(p => p.GetIndexParameters().Length == 0).ToArray();
            return new CalibrationTableSource(title, properties.Select(p => p.Name), token => read(token).Select(item =>
                new CalibrationTableRow(properties.ToDictionary(p => p.Name, p => p.GetValue(item)), payload == null ? item : payload(item))));
        }

        internal TablePage Page(TableQuery query, CancellationToken cancellation, int pageSize = 250)
        {
            if (pageSize < 1 || pageSize > 1000 || query.Minimum > query.Maximum) { throw new ArgumentException("Некорректные границы таблицы."); }
            Comparison<TableCursor> compare = (a, b) => Compare(a, b, query.SortDirection);
            SortedSet<(TableCursor Cursor, CalibrationTableRow Row)> page = new SortedSet<(TableCursor, CalibrationTableRow)>(
                Comparer<(TableCursor Cursor, CalibrationTableRow Row)>.Create((a, b) => compare(a.Cursor, b.Cursor)));
            long total = 0, visible = 0;
            foreach (CalibrationTableRow row in _read(cancellation))
            {
                cancellation.ThrowIfCancellationRequested(); long ordinal = total++;
                if (!Matches(row, query)) { continue; } visible++;
                row.Values.TryGetValue(query.SortColumn, out object sort);
                TableCursor cursor = new TableCursor(query.SortDirection == 0 ? null : sort, ordinal);
                if (query.After != null && compare(cursor, query.After) <= 0) { continue; }
                page.Add((cursor, row)); if (page.Count > pageSize + 1) { page.Remove(page.Max); }
            }
            (TableCursor Cursor, CalibrationTableRow Row)[] items = page.Take(pageSize).ToArray();
            return new TablePage(items.Select(p => p.Row).ToArray(), visible, total, page.Count > pageSize ? items.Last().Cursor : null);
        }

        private static int Compare(TableCursor a, TableCursor b, int direction)
        {
            int result = 0;
            if (direction != 0)
            {
                if (a.Value == null) { result = b.Value == null ? 0 : -1; }
                else if (b.Value == null) { result = 1; }
                else if (a.Value is IComparable comparable) { result = comparable.CompareTo(b.Value); }
                else { result = string.CompareOrdinal(a.Value.ToString(), b.Value.ToString()); }
            }
            return result == 0 ? a.Ordinal.CompareTo(b.Ordinal) : direction * Math.Sign(result);
        }
        private static bool Matches(CalibrationTableRow row, TableQuery q)
        {
            if (!string.IsNullOrWhiteSpace(q.Text) && !row.Values.Values.Any(v => Convert.ToString(v, CultureInfo.InvariantCulture)?.Contains(q.Text, StringComparison.OrdinalIgnoreCase) == true)) { return false; }
            if (!string.IsNullOrWhiteSpace(q.TimeRange) && (!row.Values.TryGetValue("TimeRangeId", out object range) || !Convert.ToString(range).Contains(q.TimeRange, StringComparison.OrdinalIgnoreCase))) { return false; }
            if (q.Direction != "Any")
            {
                if (!row.Values.TryGetValue("Direction", out object direction)) { row.Values.TryGetValue("Side", out direction); }
                if (Convert.ToString(direction) != q.Direction) { return false; }
            }
            if (q.Minimum.HasValue || q.Maximum.HasValue)
            {
                if (!row.Values.TryGetValue(q.NumericColumn, out object value) || value == null ||
                    !decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ||
                    q.Minimum.HasValue && number < q.Minimum || q.Maximum.HasValue && number > q.Maximum) { return false; }
            }
            return true;
        }
    }
}
