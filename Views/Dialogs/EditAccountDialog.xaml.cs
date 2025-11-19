using Finly.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace Finly.Views.Dialogs
{
    public partial class EditAccountDialog : Window
    {
        public BankAccountModel Model { get; }

        // ====== OPCJE BANKÓW DLA COMBOBOXA ======
        private sealed class BankOption
        {
            public string Name { get; set; } = "";
            public string LogoPath { get; set; } = "";
        }

        private readonly List<BankOption> _bankOptions = new();

        public EditAccountDialog(BankAccountModel model)
        {
            InitializeComponent();
            Model = model ?? throw new ArgumentNullException(nameof(model));

            // Wypełnij listę banków (z logotypami)
            InitBankSelection();

            // Ustaw wartości z modelu
            AccountNameBox.Text = Model.AccountName ?? string.Empty;
            IbanBox.Text = Model.Iban ?? string.Empty;
            BalanceBox.Text = Model.Balance.ToString("N2", CultureInfo.CurrentCulture);

            // Ustaw wybrany bank na podstawie modelu
            SetSelectedBank(Model.BankName);
        }

        // ====== BANKI ======

        private void InitBankSelection()
        {
            _bankOptions.Clear();

            _bankOptions.Add(new BankOption
            {
                Name = "Revolut",
                LogoPath = "pack://application:,,,/Assets/Banks/revolutlogo.png"
            });
            _bankOptions.Add(new BankOption
            {
                Name = "PKO Bank Polski",
                LogoPath = "pack://application:,,,/Assets/Banks/pkobplogo.jpg"
            });
            _bankOptions.Add(new BankOption
            {
                Name = "mBank",
                LogoPath = "pack://application:,,,/Assets/Banks/mbanklogo.jpg"
            });
            _bankOptions.Add(new BankOption
            {
                Name = "Bank Pekao",
                LogoPath = "pack://application:,,,/Assets/Banks/pekaologo.jpg"
            });
            _bankOptions.Add(new BankOption
            {
                Name = "ING Bank Śląski",
                LogoPath = "pack://application:,,,/Assets/Banks/inglogo.png"
            });
            _bankOptions.Add(new BankOption
            {
                Name = "Santander Bank Polska",
                LogoPath = "pack://application:,,,/Assets/Banks/santanderlogo.png"
            });
            _bankOptions.Add(new BankOption
            {
                Name = "Alior Bank",
                LogoPath = "pack://application:,,,/Assets/Banks/aliorbanklogo.png"
            });
            _bankOptions.Add(new BankOption
            {
                Name = "Millennium",
                LogoPath = "pack://application:,,,/Assets/Banks/milleniumlogo.png"
            });

            // Pozycja domyślna „Inny bank”
            _bankOptions.Add(new BankOption
            {
                Name = "Inny bank",
                LogoPath = "pack://application:,,,/Assets/Banks/innybank.png"
            });

            BankCombo.ItemsSource = _bankOptions;
        }

        private void SetSelectedBank(string? bankName)
        {
            if (!_bankOptions.Any())
                InitBankSelection();

            if (string.IsNullOrWhiteSpace(bankName))
            {
                BankCombo.SelectedItem = _bankOptions.Last(); // Inny bank
                return;
            }

            var match = _bankOptions.FirstOrDefault(b =>
                string.Equals(b.Name, bankName, StringComparison.OrdinalIgnoreCase));

            BankCombo.SelectedItem = match ?? _bankOptions.Last();
        }

        // ====== PRZYCISKI ======

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(
                    (BalanceBox.Text ?? "").Replace(" ", ""),
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var balance) || balance < 0)
            {
                MessageBox.Show("Podaj poprawne saldo (≥ 0).",
                    "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var selectedOption = BankCombo.SelectedItem as BankOption;
            var finalBankName = selectedOption?.Name ?? "Inny bank";

            Model.BankName = finalBankName;
            Model.AccountName = (AccountNameBox.Text ?? "").Trim();
            Model.Iban = (IbanBox.Text ?? "").Trim();
            Model.Currency = string.IsNullOrWhiteSpace(Model.Currency) ? "PLN" : Model.Currency;
            Model.Balance = balance;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}




