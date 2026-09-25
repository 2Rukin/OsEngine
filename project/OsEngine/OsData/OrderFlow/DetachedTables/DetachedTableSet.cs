/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OsEngine.OsData.OrderFlow.DetachedTables
{
    /// <summary>Explicit screen-owned registrations; disposing closes views without disposing screen models or triggering commands.</summary>
    internal sealed class DetachedTableSet : IDisposable
    {
        private readonly Dictionary<FrameworkElement, DetachedTableRegistration> _tables = new Dictionary<FrameworkElement, DetachedTableRegistration>();
        private readonly FrameworkElement _source;
        private readonly Action<Exception> _error;

        internal DetachedTableSet(FrameworkElement source, Action<Exception> error) { _source = source; _error = error; }

        internal DetachedTableRegistration Add(FrameworkElement table, string title, Func<string> context = null, Action focus = null, Func<FrameworkElement> tools = null)
        {
            if (_tables.ContainsKey(table)) { throw new InvalidOperationException("Table already registered."); }
            DetachedTableRegistration registration = new DetachedTableRegistration(_source, table, title, _error, context, focus, tools);
            _tables.Add(table, registration); return registration;
        }

        internal void Open(FrameworkElement table) { if (_tables.TryGetValue(table, out DetachedTableRegistration registration)) { registration.Open(); } }

        public void Dispose() { foreach (DetachedTableRegistration registration in _tables.Values) { registration.Dispose(); } _tables.Clear(); }

        internal Button Command(Button source, Action action = null)
        {
            Button button = new Button { Height = double.NaN, MinHeight = 30, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3), Tag = (source, action) };
            BindingOperations.SetBinding(button, ContentControl.ContentProperty, new Binding(nameof(Button.Content)) { Source = source });
            BindingOperations.SetBinding(button, UIElement.IsEnabledProperty, new Binding(nameof(Button.IsEnabled)) { Source = source });
            BindingOperations.SetBinding(button, FrameworkElement.ToolTipProperty, new Binding(nameof(Button.ToolTip)) { Source = source });
            BindingOperations.SetBinding(button, UIElement.VisibilityProperty, new Binding(nameof(Button.Visibility)) { Source = source });
            button.Click += CommandClick; return button;
        }

        private void CommandClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button || !button.IsEnabled || button.Tag is not ValueTuple<Button, Action> command || !command.Item1.IsEnabled) { return; }
                if (command.Item2 != null) { command.Item2(); }
                else { command.Item1.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, command.Item1)); }
            }
            catch (Exception error) { _error(error); }
        }

        internal static TextBox Search(TextBox source)
        {
            TextBox target = new TextBox { MinWidth = 220, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
            BindingOperations.SetBinding(target, TextBox.TextProperty, new Binding(nameof(TextBox.Text)) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            BindingOperations.SetBinding(target, FrameworkElement.ToolTipProperty, new Binding(nameof(TextBox.ToolTip)) { Source = source });
            BindingOperations.SetBinding(target, UIElement.IsEnabledProperty, new Binding(nameof(TextBox.IsEnabled)) { Source = source });
            BindingOperations.SetBinding(target, UIElement.VisibilityProperty, new Binding(nameof(TextBox.Visibility)) { Source = source });
            BindingOperations.SetBinding(target, TextBox.IsReadOnlyProperty, new Binding(nameof(TextBox.IsReadOnly)) { Source = source });
            return target;
        }

        internal static ComboBox Choice(ComboBox source)
        {
            ComboBox target = new ComboBox { MinWidth = 140, Margin = new Thickness(3), DisplayMemberPath = source.DisplayMemberPath, SelectedValuePath = source.SelectedValuePath };
            target.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ComboBox.ItemsSource)) { Source = source });
            target.SetBinding(ComboBox.SelectedIndexProperty, new Binding(nameof(ComboBox.SelectedIndex)) { Source = source, Mode = BindingMode.TwoWay });
            target.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(ComboBox.IsEnabled)) { Source = source });
            target.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(ComboBox.Visibility)) { Source = source });
            target.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ComboBox.ToolTip)) { Source = source });
            return target;
        }

        internal Button ActionButton(string title, Action action)
        {
            Button source = new Button { Content = title, ToolTip = title };
            return Command(source, action);
        }

        internal static FrameworkElement Field(string title, FrameworkElement editor)
        {
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
            TextBlock label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundWhite");
            panel.Children.Add(label); panel.Children.Add(editor); return panel;
        }
    }
}
