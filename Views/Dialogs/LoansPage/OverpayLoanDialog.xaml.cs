using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class OverpayLoanDialog : Window
    {
        public decimal Amount { get; private set; }

        public OverpayLoanDialog(string loanName)
        {
            InitializeComponent();
            TitleText.Text = "Nadpłata kredytu";
            HeaderText.Text = $"Nadpłata – {loanName}";
            Loaded += (_, __) => AmountTextBox.Focus();
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

        private void AmountTextBox_TextChanged(object sender, TextChangedEventArgs e)
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

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var raw = (AmountTextBox.Text ?? "").Trim();

            if (!TryParseDecimal(raw, out var amt) || amt <= 0m)
            {
                ShowInlineError("Podaj poprawną kwotę nadpłaty (> 0).", AmountTextBox);
                return;
            }

            Amount = Math.Round(amt, 2, MidpointRounding.AwayFromZero);
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
