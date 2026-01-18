using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Finly.Models;

namespace Finly.Views.Dialogs
{
    public partial class EditBankAccountDialog : Window
    {
        public enum DialogMode { Add, Edit }

        public BankAccountModel Result { get; private set; } = new();
        private DialogMode _mode;

        public EditBankAccountDialog()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                // domyślnie zaznacz "Inny bank"
                SelectBankInCombo("Inny bank");
            };
        }

        public void SetMode(DialogMode mode)
        {
            _mode = mode;

            HeaderTitleText.Text = mode == DialogMode.Add ? "Dodaj konto bankowe" : "Edytuj konto bankowe";
            HeaderSubtitleText.Text = mode == DialogMode.Add
                ? "Uzupełnij dane konta i zapisz."
                : "Zmień dane konta i zapisz zmiany.";
        }

        public void Load(BankAccountModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            // kopiujemy, żeby dialog nie mutował obiektu wejściowego
            Result = new BankAccountModel
            {
                Id = model.Id,
                UserId = model.UserId,
                ConnectionId = model.ConnectionId,
                BankName = model.BankName ?? "",
                AccountName = model.AccountName ?? "",
                Iban = model.Iban ?? "",
                Currency = string.IsNullOrWhiteSpace(model.Currency) ? "PLN" : model.Currency,
                Balance = model.Balance,
                LastSync = model.LastSync
            };

            AccountNameBox.Text = Result.AccountName;
            IbanBox.Text = Result.Iban;
            BalanceBox.Text = Result.Balance.ToString("N2", CultureInfo.CurrentCulture);

            // bank: jeśli nie ma na liście, dodaj dynamicznie (bez dodatkowego pola)
            if (!SelectBankInCombo(Result.BankName))
            {
                EnsureCustomBankItem(Result.BankName);
                SelectBankInCombo(Result.BankName);
            }
        }

        // ===== TitleBar =====
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ===== Bank combo helpers =====
        private static string ExtractBankName(object? item)
        {
            if (item is ComboBoxItem ci)
            {
                if (ci.Content is string s) return s.Trim();

                if (ci.Content is StackPanel sp)
                {
                    foreach (var child in sp.Children)
                        if (child is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                            return tb.Text.Trim();
                }
            }
            return "";
        }

        private bool SelectBankInCombo(string bankName)
        {
            foreach (var item in BankCombo.Items)
            {
                if (ExtractBankName(item).Equals(bankName, StringComparison.OrdinalIgnoreCase))
                {
                    BankCombo.SelectedItem = item;
                    return true;
                }
            }
            return false;
        }

        private void EnsureCustomBankItem(string bankName)
        {
            var name = (bankName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            // nie duplikuj
            foreach (var item in BankCombo.Items)
                if (ExtractBankName(item).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return;

            // tworzymy item z generycznym logo
            var ci = new ComboBoxItem
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Image
                        {
                            Width = 20,
                            Height = 20,
                            Margin = new Thickness(0,0,8,0),
                            Source = new System.Windows.Media.Imaging.BitmapImage(
                                new Uri("pack://application:,,,/Assets/Banks/innybank.png", UriKind.Absolute))
                        },
                        new TextBlock { Text = name }
                    }
                }
            };

            // wstaw przed "Inny bank" (zakładamy, że jest ostatni)
            var insertIndex = Math.Max(0, BankCombo.Items.Count - 1);
            BankCombo.Items.Insert(insertIndex, ci);
        }

        // ===== IBAN =====
        private void IbanBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            IbanHintText.Visibility = Visibility.Collapsed;
            IbanHintText.Text = "";
        }

        private static bool ValidatePolishIban(string input, out string normalized, out string? error)
        {
            normalized = "";
            error = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Numer IBAN jest pusty.";
                return false;
            }

            var s = input.ToUpperInvariant().Replace(" ", "");

            if (!s.StartsWith("PL"))
            {
                error = "Polski IBAN musi zaczynać się od 'PL'.";
                return false;
            }

            if (s.Length != 28)
            {
                error = "Polski IBAN musi mieć dokładnie 28 znaków (PL + 26 cyfr).";
                return false;
            }

            for (int i = 2; i < 28; i++)
            {
                if (!char.IsDigit(s[i]))
                {
                    error = "Po 'PL' w numerze IBAN mogą występować tylko cyfry.";
                    return false;
                }
            }

            normalized = s;
            return true;
        }

        private static string FormatPolishIban(string normalized)
        {
            var s = normalized.ToUpperInvariant().Replace(" ", "");
            if (s.Length != 28 || !s.StartsWith("PL")) return normalized;

            string country = s.Substring(0, 2);
            string check = s.Substring(2, 2);
            string rest = s.Substring(4);

            var parts = new List<string> { country + check };
            for (int i = 0; i < rest.Length; i += 4)
            {
                int len = Math.Min(4, rest.Length - i);
                parts.Add(rest.Substring(i, len));
            }

            return string.Join(" ", parts);
        }

        // ===== Balance formatting =====
        private void BalanceBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var txt = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt) || txt == 0m.ToString("N2", CultureInfo.CurrentCulture))
                tb.Clear();

            tb.SelectAll();
        }

        private void BalanceBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var txt = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt))
            {
                tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
                return;
            }

            if (decimal.TryParse(txt, NumberStyles.Number, CultureInfo.CurrentCulture, out var v))
                tb.Text = v.ToString("N2", CultureInfo.CurrentCulture);
            else
                tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
        }

        // ===== Buttons =====
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // saldo
            var rawBal = (BalanceBox.Text ?? "").Replace(" ", "");
            if (!decimal.TryParse(rawBal, NumberStyles.Number, CultureInfo.CurrentCulture, out var bal) || bal < 0)
            {
                MessageBox.Show("Podaj poprawne saldo (≥ 0).", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // iban
            var rawIban = (IbanBox.Text ?? "").Trim();
            string finalIban = "";
            if (!string.IsNullOrEmpty(rawIban))
            {
                if (!ValidatePolishIban(rawIban, out var normalized, out var error))
                {
                    IbanHintText.Text = error ?? "Nieprawidłowy numer IBAN.";
                    IbanHintText.Visibility = Visibility.Visible;
                    return;
                }
                finalIban = FormatPolishIban(normalized);
            }

            // bank (bez dodatkowego pola)
            var bankFromCombo = ExtractBankName(BankCombo.SelectedItem);
            var finalBank = string.IsNullOrWhiteSpace(bankFromCombo) ? "Inny bank" : bankFromCombo;

            Result.BankName = finalBank;
            Result.AccountName = (AccountNameBox.Text ?? "").Trim();
            Result.Iban = finalIban;
            Result.Currency = "PLN";
            Result.Balance = bal;

            DialogResult = true;
            Close();
        }
    }
}
