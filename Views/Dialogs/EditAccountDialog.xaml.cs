using Finly.Models;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Views.Dialogs
{
    public partial class EditAccountDialog : Window
    {
        public BankAccountModel Model { get; }

        public EditAccountDialog(BankAccountModel model)
        {
            InitializeComponent();
            Model = model ?? throw new ArgumentNullException(nameof(model));

            // Wypełnij ComboBox listą banków (z XAML)
            InitBankSelection();

            // Ustaw wartości z modelu
            AccountNameBox.Text = Model.AccountName ?? string.Empty;
            IbanBox.Text = Model.Iban ?? string.Empty;
            BalanceBox.Text = Model.Balance.ToString("N2", CultureInfo.CurrentCulture);

            // Ustaw wybrany bank
            SetSelectedBank(Model.BankName);

            BankCombo.SelectionChanged += BankCombo_SelectionChanged;
            BankCombo_SelectionChanged(BankCombo, null);
        }

        // ====== BANKI ======

        private void InitBankSelection()
        {
            // Jeśli w XAML coś zmienisz / dodasz, nie trzeba ruszać kodu.
            if (BankCombo.Items.Count == 0)
            {
                BankCombo.Items.Add(new ComboBoxItem { Content = "Inny bank" });
            }
        }

        private void SetSelectedBank(string? bankName)
        {
            if (string.IsNullOrWhiteSpace(bankName))
            {
                BankCombo.SelectedIndex = BankCombo.Items.Count - 1; // Inny bank
                BankNameBox.Text = "";
                return;
            }

            // Szukamy pozycji o takiej samej nazwie
            foreach (var item in BankCombo.Items.Cast<ComboBoxItem>())
            {
                if (string.Equals(item.Content?.ToString(), bankName, StringComparison.OrdinalIgnoreCase))
                {
                    BankCombo.SelectedItem = item;
                    BankNameBox.Text = bankName;
                    return;
                }
            }

            // Nie znaleziono – traktujemy jako „Inny bank”, ale zachowujemy nazwę w polu
            BankCombo.SelectedIndex = BankCombo.Items.Count - 1;
            BankNameBox.Text = bankName;
        }

        private void BankCombo_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            var selected = (BankCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();

            if (string.Equals(selected, "Inny bank", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(selected))
            {
                BankNameBox.IsEnabled = true;
                BankNameBox.Opacity = 1.0;
            }
            else
            {
                BankNameBox.Text = selected;
                BankNameBox.IsEnabled = false;
                BankNameBox.Opacity = 0.7;
            }
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

            var bankNameFromBox = (BankNameBox.Text ?? "").Trim();
            var selectedFromCombo = (BankCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            var finalBankName = !string.IsNullOrWhiteSpace(bankNameFromBox)
                ? bankNameFromBox
                : selectedFromCombo;

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


