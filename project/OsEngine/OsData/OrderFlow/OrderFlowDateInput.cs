/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Language;
using System;
using System.Windows.Controls;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Retains date-edit errors across WPF text normalization so a failed edit cannot select the full file.</summary>
    /// <remarks>
    /// The owning window creates, reads and disposes this guard on its dispatcher thread.
    /// A valid selection or explicit Clear recovers from an error; automatic empty text does not.
    /// Contract: ORDER-FLOW-DATA-001. No file access or replay occurs here.
    /// </remarks>
    internal sealed class OrderFlowDateInput : IDisposable
    {
        private readonly DatePicker _picker;
        private bool _invalidEdit;

        #region Public methods

        /// <summary>Subscribes to this window-owned date picker; the caller must dispose on window close.</summary>
        public OrderFlowDateInput(DatePicker picker)
        {
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _picker.DateValidationError += OnValidationError;
            _picker.SelectedDateChanged += OnSelectionChanged;
        }

        /// <summary>Returns the selected date or an intentionally empty field; rejects unresolved typed errors.</summary>
        /// <exception cref="ArgumentException">An edit failed or nonempty text has no parsed selection.</exception>
        public DateTime? ReadDate()
        {
            if (_invalidEdit || (!string.IsNullOrWhiteSpace(_picker.Text) && !_picker.SelectedDate.HasValue))
            {
                throw new ArgumentException(OsLocalization.ConvertToLocString(
                    "Eng:Invalid date. Select a valid date or press Full file._Ru:Дата введена неверно. Выберите корректную дату или нажмите Весь файл._"));
            }
            if (string.IsNullOrWhiteSpace(_picker.Text))
            {
                _picker.SelectedDate = null;
                return null;
            }
            return _picker.SelectedDate;
        }

        /// <summary>Explicitly clears both selection and edit error for an intentional full-file request.</summary>
        public void Clear()
        {
            _picker.SelectedDate = null;
            _picker.Text = string.Empty;
            _invalidEdit = false;
        }

        /// <summary>Detaches control handlers on the owning dispatcher; repeated calls are harmless.</summary>
        public void Dispose()
        {
            _picker.DateValidationError -= OnValidationError;
            _picker.SelectedDateChanged -= OnSelectionChanged;
        }

        #endregion

        #region Control events

        private void OnValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
        {
            e.ThrowException = false;
            _invalidEdit = true;
            _picker.SelectedDate = null;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_picker.SelectedDate.HasValue) { _invalidEdit = false; }
        }

        #endregion
    }
}
