/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>
    /// Moves one chart surface between UI-thread hosts without recreating controls or results.
    /// Owner closes the separate window before disposal. Direct binding sources survive changes of namescope.
    /// </summary>
    internal sealed class OrderFlowChartHost : IDisposable
    {
        #region Surface lifetime

        private readonly ContentControl _home;
        private ContentControl _current;
        private object _surface;

        public OrderFlowChartHost(ContentControl home)
        {
            _home = _current = home;
            _surface = home.Content ?? throw new ArgumentException("Chart surface is required.", nameof(home));
        }

        /// <summary>Moves the same surface into an empty target. A failed attach restores the preceding parent.</summary>
        public void MoveTo(ContentControl target)
        {
            _home.VerifyAccess();
            if (_surface == null) { throw new ObjectDisposedException(nameof(OrderFlowChartHost)); }
            if (target == _current) { return; }
            if (target.Content != null) { throw new InvalidOperationException("Target already has content."); }
            ContentControl previous = _current;
            previous.Content = null;
            try { target.Content = _surface; _current = target; }
            catch { target.Content = null; previous.Content = _surface; throw; }
        }

        /// <summary>Returns the same surface to its original host; repeated restoration is harmless.</summary>
        public void Restore() { MoveTo(_home); }

        /// <summary>Detaches the surface without restoring it into a closing owner. Call only on the UI thread.</summary>
        public void Dispose()
        {
            _home.VerifyAccess();
            if (_surface == null) { return; }
            _current.Content = null;
            _surface = null;
            _current = null;
        }

        #endregion

        #region Stable bindings

        /// <summary>Binds chart toolbar controls to existing settings objects, independent of Window namescopes.</summary>
        public static void BindSettings(ComboBox timeFrame, ComboBox settingsTimeFrame, Slider scale, Slider settingsScale, TextBlock scaleText)
        {
            BindingOperations.SetBinding(timeFrame, ItemsControl.ItemsSourceProperty,
                new Binding(nameof(ItemsControl.ItemsSource)) { Source = settingsTimeFrame });
            BindingOperations.SetBinding(timeFrame, System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
                new Binding(nameof(ComboBox.SelectedValue)) { Source = settingsTimeFrame, Mode = BindingMode.TwoWay });
            BindVisualSlider(scale, settingsScale, scaleText, "{0:F1}×");
        }

        /// <summary>Binds a mirrored visual slider and readout to the settings object, surviving popout/return namescope changes.</summary>
        public static void BindVisualSlider(Slider target, Slider source, TextBlock readout, string format)
        {
            BindingOperations.SetBinding(target, System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new Binding(nameof(Slider.Value)) { Source = source, Mode = BindingMode.TwoWay });
            BindingOperations.SetBinding(readout, TextBlock.TextProperty,
                new Binding(nameof(Slider.Value)) { Source = source, StringFormat = format });
        }

        #endregion
    }
}
