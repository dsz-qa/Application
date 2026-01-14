using Finly.Helpers.Converters;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class AddValuationDialog : Window
    {
        public DateTime SelectedDate { get; private set; } = DateTime.Today;
        public decimal Value { get; private set; }

        private bool _loadedOnce;

        public AddValuationDialog(string investmentName)
        {
            try
            {
                InitializeComponent();

                // Bezpieczna inicjalizacja treści
                Loaded += (_, __) =>
                {
                    if (_loadedOnce) return; // zabezpieczenie przed podwójnym Loaded
                    _loadedOnce = true;

                    SafeSetText(HeaderTitleText, "Dodaj wycenę");
                    SafeSetText(HeaderSubText, "Inwestycja: " + (investmentName ?? string.Empty));

                    try
                    {
                        DatePicker.SelectedDate = DateTime.Today;
                    }
                    catch { /* nie ryzykujemy wyjątku */ }

                    HideInlineError();

                    try
                    {
                        Value = 0m;
                        ValueTextBox.Text = 0m.ToString("0.00", CultureInfo.CurrentCulture);
                    }
                    catch
                    {
                        // awaryjnie
                        Value = 0m;
                    }
                };
            }
            catch (Exception ex)
            {
                // Najgorszy scenariusz: okno nie może się zbudować - zamykamy bez wywalania aplikacji
                System.Diagnostics.Debug.WriteLine("AddValuationDialog init error: " + ex);
                try
                {
                    Content = new TextBlock
                    {
                        Text = "Nie można otworzyć okna wyceny:\n" + ex.Message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(16)
                    };
                }
                catch { }
            }
        }

        private static void SafeSetText(TextBlock? tb, string text)
        {
            try
            {
                if (tb != null) tb.Text = text ?? string.Empty;
            }
            catch { }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // DragMove potrafi rzucić InvalidOperationException - zawsze w try/catch
            try
            {
                if (e.ButtonState == MouseButtonState.Pressed && WindowState == WindowState.Normal)
                    DragMove();
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SafeClose(false);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SafeClose(false);
        }

        private void SafeClose(bool dialogResult)
        {
            try
            {
                DialogResult = dialogResult;
            }
            catch { /* gdy okno nie jest dialogiem */ }

            try
            {
                Close();
            }
            catch { }
        }

        private void ShowInlineError(string message, FrameworkElement? focus = null)
        {
            try
            {
                InlineErrorText.Text = message ?? "";
                InlineErrorText.Visibility = Visibility.Visible;
            }
            catch { }

            try
            {
                focus?.Focus();
            }
            catch { }
        }

        private void HideInlineError()
        {
            try
            {
                InlineErrorText.Text = "";
                InlineErrorText.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void Value_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not TextBox tb) return;

                if (!tb.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    tb.Focus();
                }
            }
            catch { }
        }

        private void Value_GotFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                HideInlineError();

                if (sender is not TextBox tb) return;

                var t = (tb.Text ?? string.Empty).Trim();

                // usuwamy separatory tysięcy / NBSP / thin space
                t = t.Replace(" ", "")
                     .Replace("\u00A0", "")
                     .Replace("\u202F", "")
                     .Replace("'", "");

                // pozwól wpisywać “gołe” liczby
                if (t.EndsWith(",00", StringComparison.Ordinal) || t.EndsWith(".00", StringComparison.Ordinal))
                    t = t[..^3];

                if (t == "0" || t == "0," || t == "0." || string.IsNullOrWhiteSpace(t))
                    tb.Clear();
                else
                    tb.Text = t;

                tb.SelectAll();
            }
            catch { }
        }

        private void Value_LostFocus(object sender, RoutedEventArgs e)
        {
            NormalizeValueText();
        }

        private void NormalizeValueText()
        {
            try
            {
                var raw = (ValueTextBox.Text ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    Value = 0m;
                    ValueTextBox.Text = 0m.ToString("0.00", CultureInfo.CurrentCulture);
                    return;
                }

                // Najpierw Twoja elastyczna logika
                if (FlexibleDecimalConverter.TryParseFlexibleDecimal(raw, out var val))
                {
                    val = Math.Round(val, 2, MidpointRounding.AwayFromZero);
                    Value = val;
                    ValueTextBox.Text = val.ToString("0.00", CultureInfo.CurrentCulture);
                    return;
                }

                // Awaryjnie: spróbuj zwykłego decimal.TryParse na bieżącej kulturze
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var fallback))
                {
                    fallback = Math.Round(fallback, 2, MidpointRounding.AwayFromZero);
                    Value = fallback;
                    ValueTextBox.Text = fallback.ToString("0.00", CultureInfo.CurrentCulture);
                    return;
                }

                // Jeśli nie da się sparsować — reset bez wyjątku
                Value = 0m;
                ValueTextBox.Text = 0m.ToString("0.00", CultureInfo.CurrentCulture);
            }
            catch
            {
                // Nigdy nie pozwól, by błąd formatowania wysypał okno
                try
                {
                    Value = 0m;
                    ValueTextBox.Text = 0m.ToString("0.00", CultureInfo.CurrentCulture);
                }
                catch { }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HideInlineError();
                NormalizeValueText();

                DateTime date;

                try
                {
                    if (DatePicker.SelectedDate == null)
                    {
                        ShowInlineError("Wybierz datę.", DatePicker);
                        return;
                    }

                    date = DatePicker.SelectedDate.Value.Date;
                }
                catch
                {
                    ShowInlineError("Wybierz prawidłową datę.", DatePicker);
                    return;
                }

                if (Value <= 0m)
                {
                    ShowInlineError("Wartość musi być większa od zera.", ValueTextBox);
                    return;
                }

                SelectedDate = date;

                SafeClose(true);
            }
            catch
            {
                // absolutny bezpiecznik
                SafeClose(false);
            }
        }
    }
}
