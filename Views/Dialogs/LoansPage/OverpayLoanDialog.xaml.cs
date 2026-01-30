using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class OverpayLoanDialog : Window
    {
        public enum OverpayMode
        {
            Csv = 1,
            Manual = 2
        }

        public decimal Amount { get; private set; }
        public decimal CapitalPaid { get; private set; }

        public OverpayMode Mode { get; private set; } = OverpayMode.Csv;

        // A1
        public string? AttachedSchedulePath { get; private set; }

        // A2
        public bool ManualLowerPayment { get; private set; } = true;
        public decimal? ManualNewPayment { get; private set; }
        public int? ManualRemainingMonths { get; private set; }

        public OverpayLoanDialog(string loanName)
        {
            InitializeComponent();

            TitleText.Text = "Nadpłata kredytu";
            HeaderText.Text = $"Nadpłata – {loanName}";

            // nie polegaj na Checked eventach – wymuś stan po załadowaniu
            Loaded += (_, __) => AmountTextBox.Focus();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // podepnij eventy dopiero gdy XAML jest w pełni zainicjalizowany
            ModeCsvRadio.Checked += ModeRadio_Checked;
            ModeManualRadio.Checked += ModeRadio_Checked;

            ManualLowerPaymentRadio.Checked += ManualRadio_Checked;
            ManualShorterTermRadio.Checked += ManualRadio_Checked;

            // wymuszenie poprawnych paneli na starcie
            ApplyModeUi();
            ApplyManualUi();
        }


        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AnyInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            InlineErrorText.Text = "";
            InlineErrorText.Visibility = Visibility.Collapsed;
        }

        private void ShowInlineError(string msg, Control? focus = null)
        {
            InlineErrorText.Text = msg;
            InlineErrorText.Visibility = Visibility.Visible;
            focus?.Focus();
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            ApplyModeUi();
        }

        private void ManualRadio_Checked(object sender, RoutedEventArgs e)
        {
            ApplyManualUi();
        }

        private void ApplyModeUi()
        {
            if (CsvPanel == null || ManualPanel == null || ModeCsvRadio == null) return;

            Mode = ModeCsvRadio.IsChecked == true ? OverpayMode.Csv : OverpayMode.Manual;

            CsvPanel.Visibility = Mode == OverpayMode.Csv ? Visibility.Visible : Visibility.Collapsed;
            ManualPanel.Visibility = Mode == OverpayMode.Manual ? Visibility.Visible : Visibility.Collapsed;

            if (Mode == OverpayMode.Csv)
            {
                ManualNewPayment = null;
                ManualRemainingMonths = null;
            }
            else
            {
                ApplyManualUi();
            }
        }

        private void ApplyManualUi()
        {
            if (ManualLowerPaymentRadio == null || ManualNewPaymentPanel == null || ManualRemainingMonthsPanel == null)
                return;

            ManualLowerPayment = ManualLowerPaymentRadio.IsChecked == true;

            ManualNewPaymentPanel.Visibility = ManualLowerPayment ? Visibility.Visible : Visibility.Collapsed;
            ManualRemainingMonthsPanel.Visibility = ManualLowerPayment ? Visibility.Collapsed : Visibility.Visible;

            if (ManualLowerPayment)
            {
                ManualRemainingMonthsTextBox.Text = "";
                ManualRemainingMonths = null;
            }
            else
            {
                ManualNewPaymentTextBox.Text = "";
                ManualNewPayment = null;
            }
        }


        private void PickCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Wybierz plik harmonogramu (CSV) po nadpłacie",
                Filter = "Pliki CSV (*.csv)|*.csv|Wszystkie pliki (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                AttachedSchedulePath = dlg.FileName;

                var shortName = Path.GetFileName(AttachedSchedulePath);
                CsvPathText.Text = $"Wybrano: {shortName}";
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // ===== Kwota łączna =====
            var rawAmount = (AmountTextBox.Text ?? "").Trim();
            if (!TryParseDecimal(rawAmount, out var amt) || amt <= 0m)
            {
                ShowInlineError("Podaj poprawną kwotę wpłaty (> 0).", AmountTextBox);
                return;
            }

            // ===== Kapitał spłacony =====
            var rawCap = (CapitalTextBox.Text ?? "").Trim();
            if (!TryParseDecimal(rawCap, out var cap) || cap <= 0m)
            {
                ShowInlineError("Podaj poprawną kwotę kapitału spłaconego (> 0).", CapitalTextBox);
                return;
            }

            amt = Math.Round(amt, 2, MidpointRounding.AwayFromZero);
            cap = Math.Round(cap, 2, MidpointRounding.AwayFromZero);

            if (cap > amt)
            {
                ShowInlineError("Kapitał nie może być większy niż kwota wpłaty.", CapitalTextBox);
                return;
            }

            Amount = amt;
            CapitalPaid = cap;

            // ===== Tryb =====
            Mode = ModeCsvRadio.IsChecked == true ? OverpayMode.Csv : OverpayMode.Manual;

            if (Mode == OverpayMode.Csv)
            {
                if (string.IsNullOrWhiteSpace(AttachedSchedulePath) || !File.Exists(AttachedSchedulePath))
                {
                    ShowInlineError("W trybie CSV wybierz plik harmonogramu.", null);
                    return;
                }

                // tryb CSV -> manual wartości czyścimy
                ManualNewPayment = null;
                ManualRemainingMonths = null;
            }
            else
            {
                // Manual
                ManualLowerPayment = ManualLowerPaymentRadio.IsChecked == true;

                if (ManualLowerPayment)
                {
                    var rawNewPay = (ManualNewPaymentTextBox.Text ?? "").Trim();
                    if (!TryParseDecimal(rawNewPay, out var np) || np <= 0m)
                    {
                        ShowInlineError("Podaj poprawną nową ratę (> 0).", ManualNewPaymentTextBox);
                        return;
                    }

                    ManualNewPayment = Math.Round(np, 2, MidpointRounding.AwayFromZero);
                    ManualRemainingMonths = null;
                }
                else
                {
                    var rawMonths = (ManualRemainingMonthsTextBox.Text ?? "").Trim();
                    if (!int.TryParse(rawMonths, out var m) || m <= 0)
                    {
                        ShowInlineError("Podaj poprawną liczbę pozostałych rat (> 0).", ManualRemainingMonthsTextBox);
                        return;
                    }

                    ManualRemainingMonths = m;
                    ManualNewPayment = null;
                }
            }

            DialogResult = true;
            Close();
        }

        private static bool TryParseDecimal(string raw, out decimal value)
        {
            value = 0m;
            raw = (raw ?? "").Trim();

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Replace("\u00A0", " ").Replace("\u202F", " ").Replace(" ", "");

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                return true;

            return decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }
    }
}
