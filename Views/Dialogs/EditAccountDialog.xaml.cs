using System;
using System.Globalization;
using System.Windows;
using Finly.Models;
using Finly.Services;

namespace Finly.Views.Dialogs
{
    public partial class EditAccountDialog : Window
    {
        public BankAccountModel Model { get; }

        public EditAccountDialog(BankAccountModel model = null!)
        {
            InitializeComponent();

            Model = model ?? new BankAccountModel { Currency = "PLN" };

            // UI -> wstępne wartości
            NameBox.Text = Model.AccountName ?? "";
            IbanBox.Text = Model.Iban ?? "";

            CurComboBox.ItemsSource = new[] { "PLN", "EUR", "USD", "GBP" };
            CurComboBox.SelectedItem = string.IsNullOrWhiteSpace(Model.Currency) ? "PLN" : Model.Currency;

            BalanceBox.Text = Model.Balance.ToString(CultureInfo.CurrentCulture);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Walidacja nazwy
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ToastService.Warning("Podaj nazwę konta.");
                NameBox.Focus();
                return;
            }

            // Parsowanie salda (działa z przecinkiem lub kropką)
            var raw = (BalanceBox.Text ?? "").Trim();
            // Normalizuj znak dziesiętny do lokalnego
            var dec = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            raw = raw.Replace(" ", "").Replace(",", dec).Replace(".", dec);

            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var bal))
            {
                ToastService.Error("Niepoprawne saldo.");
                BalanceBox.Focus();
                return;
            }

            // Zapis do modelu
            Model.AccountName = NameBox.Text.Trim();
            Model.Iban = IbanBox.Text?.Trim() ?? "";
            Model.Currency = CurComboBox.SelectedItem?.ToString() ?? "PLN";
            Model.Balance = bal;

            DialogResult = true;
            Close();
        }
    }
}
