/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms.Integration;
using Forms = System.Windows.Forms;

namespace OsEngine.OsData.Statistics
{
    public sealed class StatisticsSortableGrid : IDisposable
    {
        private readonly WindowsFormsHost _host;
        private Func<int, int, object> _getCell;
        private int[] _rows = new int[0];
        private int _sortColumn = -1;
        private bool _descending;
        private bool _disposed;
        private CancellationTokenSource _sortCancellation;

        public StatisticsSortableGrid(WindowsFormsHost host, string[] headers, Func<int, int, object> getCell)
        {
            _host = host;
            _getCell = getCell;
            Grid = DataGridFactory.GetDataGridView(Forms.DataGridViewSelectionMode.FullRowSelect, Forms.DataGridViewAutoSizeRowsMode.None);
            Grid.ReadOnly = true;
            Grid.AllowUserToAddRows = false;
            Grid.AllowUserToDeleteRows = false;
            Grid.VirtualMode = true;
            Grid.ScrollBars = Forms.ScrollBars.Both;
            Grid.Dock = Forms.DockStyle.Fill;
            Grid.AutoSizeColumnsMode = Forms.DataGridViewAutoSizeColumnsMode.None;
            foreach (string header in headers)
            {
                Grid.Columns.Add(new Forms.DataGridViewTextBoxColumn { HeaderText = header, Width = 150, SortMode = Forms.DataGridViewColumnSortMode.Programmatic });
            }
            Grid.CellValueNeeded += Grid_CellValueNeeded;
            Grid.CellFormatting += Grid_CellFormatting;
            Grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            Grid.DataError += Grid_DataError;
            _host.Child = Grid;
        }

        public Forms.DataGridView Grid { get; private set; }

        public int SelectedSourceIndex
        {
            get
            {
                int row = Grid.CurrentCell == null ? -1 : Grid.CurrentCell.RowIndex;
                return row >= 0 && row < _rows.Length ? _rows[row] : -1;
            }
        }

        #region Data and appearance

        public void SetCount(int count)
        {
            CancelSort();
            Grid.RowCount = 0;
            _rows = new int[count];
            for (int index = 0; index < count; index++)
            {
                _rows[index] = index;
            }
            _sortColumn = -1;
            foreach (Forms.DataGridViewColumn column in Grid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = Forms.SortOrder.None;
            }
            Grid.RowCount = count;
            Grid.Invalidate();
        }

        public void RefreshHeaders(string[] headers)
        {
            for (int index = 0; index < headers.Length && index < Grid.Columns.Count; index++)
            {
                Grid.Columns[index].HeaderText = headers[index];
            }
            Grid.Invalidate();
        }

        public void ApplyTheme()
        {
            DataGridFactory.ApplyTheme(Grid);
        }

        private void Grid_CellValueNeeded(object sender, Forms.DataGridViewCellValueEventArgs e)
        {
            try
            {
                if (!_disposed && e.RowIndex >= 0 && e.RowIndex < _rows.Length)
                {
                    e.Value = _getCell(_rows[e.RowIndex], e.ColumnIndex);
                }
            }
            catch (Exception error)
            {
                Log(error);
            }
        }

        private void Grid_CellFormatting(object sender, Forms.DataGridViewCellFormattingEventArgs e)
        {
            try
            {
                if (e.Value is DateTime date)
                {
                    e.Value = date.ToString(string.IsNullOrEmpty(e.CellStyle.Format) ? "yyyy-MM-dd HH:mm:ss" : e.CellStyle.Format, CultureInfo.CurrentCulture);
                    e.FormattingApplied = true;
                }
                else if (e.Value is bool flag)
                {
                    e.Value = flag ? OsLocalization.ConvertToLocString("Eng:Yes_Ru:Да_") : OsLocalization.ConvertToLocString("Eng:No_Ru:Нет_");
                    e.FormattingApplied = true;
                }
            }
            catch (Exception error)
            {
                Log(error);
            }
        }

        private void Grid_DataError(object sender, Forms.DataGridViewDataErrorEventArgs e)
        {
            Log(e.Exception ?? new InvalidOperationException(e.ToString()));
        }

        #endregion

        #region Sorting

        private void Grid_ColumnHeaderMouseClick(object sender, Forms.DataGridViewCellMouseEventArgs e)
        {
            try
            {
                if (e.ColumnIndex < 0 || _disposed)
                {
                    return;
                }
                CancelSort();
                int columnIndex = e.ColumnIndex;
                bool descending = _sortColumn == columnIndex && !_descending;
                // Snapshot typed keys on the UI thread; only independent arrays leave it.
                object[] keys = new object[_rows.Length];
                int[] sortedRows = new int[_rows.Length];
                for (int index = 0; index < keys.Length; index++)
                {
                    keys[index] = _getCell(index, columnIndex);
                    sortedRows[index] = index;
                }
                CancellationTokenSource cancellation = new CancellationTokenSource();
                _sortCancellation = cancellation;
                Grid.UseWaitCursor = true;
                Task.Run(delegate
                {
                    try
                    {
                        Array.Sort(sortedRows, delegate(int first, int second)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            int comparison = Compare(keys[first], keys[second]);
                            if (comparison == 0)
                            {
                                return first.CompareTo(second);
                            }
                            return descending ? -Math.Sign(comparison) : comparison;
                        });
                        PublishSort(cancellation, sortedRows, columnIndex, descending);
                    }
                    catch (Exception error)
                    {
                        if (!cancellation.IsCancellationRequested)
                        {
                            Log(error);
                        }
                    }
                    finally
                    {
                        FinishSort(cancellation);
                        cancellation.Dispose();
                    }
                });
            }
            catch (Exception error)
            {
                Log(error);
            }
        }

        private void PublishSort(CancellationTokenSource cancellation, int[] rows, int column, bool descending)
        {
            if (_host.Dispatcher.HasShutdownStarted)
            {
                return;
            }
            _host.Dispatcher.Invoke(new Action(delegate
            {
                if (_disposed || _sortCancellation != cancellation || cancellation.IsCancellationRequested)
                {
                    return;
                }
                _rows = rows;
                _sortColumn = column;
                _descending = descending;
                foreach (Forms.DataGridViewColumn gridColumn in Grid.Columns)
                {
                    gridColumn.HeaderCell.SortGlyphDirection = gridColumn.Index == column ? (descending ? Forms.SortOrder.Descending : Forms.SortOrder.Ascending) : Forms.SortOrder.None;
                }
                Grid.CurrentCell = null;
                Grid.ClearSelection();
                Grid.Invalidate();
            }));
        }

        private void FinishSort(CancellationTokenSource cancellation)
        {
            try
            {
                if (!_host.Dispatcher.HasShutdownStarted)
                {
                    _host.Dispatcher.Invoke(new Action(delegate
                    {
                        if (!_disposed && _sortCancellation == cancellation)
                        {
                            _sortCancellation = null;
                            Grid.UseWaitCursor = false;
                        }
                    }));
                }
            }
            catch (Exception error)
            {
                Log(error);
            }
        }

        private static int Compare(object first, object second)
        {
            if (first == null) return second == null ? 0 : -1;
            if (second == null) return 1;
            if (first.GetType() == second.GetType() && first is IComparable comparable)
            {
                return comparable.CompareTo(second);
            }
            if (first is IConvertible && second is IConvertible && !(first is string) && !(second is string))
            {
                return Convert.ToDecimal(first, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(second, CultureInfo.InvariantCulture));
            }
            return string.Compare(first.ToString(), second.ToString(), StringComparison.CurrentCulture);
        }

        private void CancelSort()
        {
            _sortCancellation?.Cancel();
            _sortCancellation = null;
            Grid.UseWaitCursor = false;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelSort();
            Grid.CellValueNeeded -= Grid_CellValueNeeded;
            Grid.CellFormatting -= Grid_CellFormatting;
            Grid.ColumnHeaderMouseClick -= Grid_ColumnHeaderMouseClick;
            Grid.DataError -= Grid_DataError;
            DataGridFactory.ClearLinks(Grid);
            _host.Child = null;
            Grid.RowCount = 0;
            Grid.Columns.Clear();
            Grid.Dispose();
            _getCell = null;
            _rows = new int[0];
        }

        private static void Log(Exception error)
        {
            ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
        }
    }
}
