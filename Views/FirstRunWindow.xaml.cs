using Finly.Models;
using Finly.Services.Features;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views
{
    public partial class FirstRunWindow : Window
    {
        private readonly int _userId;

        public FirstRunWindow(int userId)
        {
            InitializeComponent();
            _userId = userId;
        }

        // Czyści "0,00" po kliknięciu w pole, a gdy puste – przywraca
        private void AmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var text = tb.Text.Trim();
                if (text == "0" || text == "0,0" || text == "0,00" || text == "0.00")
                {
                    tb.Text = string.Empty;
                }
                tb.SelectAll();
            }
        }

        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    tb.Text = "0,00";
                }
            }
        }

        // =================== Pasek tytułu / rozmiar okna ===================

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // okno zajmuje obszar roboczy (bez paska zadań)
            var workArea = SystemParameters.WorkArea;

            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaxRestore_Click(sender, e);
                return;
            }

            try { DragMove(); }
            catch { /* ignoruj */ }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // =================== Logika formularza ===================

        private bool TryReadValues(out decimal freeCash, out decimal savedCash, out decimal bankTotal)
        {
            freeCash = 0m;
            savedCash = 0m;
            bankTotal = 0m;

            var culture = CultureInfo.CurrentCulture;
            bool ok = true;
            string errors = "";

            // Wolna gotówka
            if (!string.IsNullOrWhiteSpace(FreeCashBox.Text))
            {
                if (!decimal.TryParse(FreeCashBox.Text, NumberStyles.Any, culture, out freeCash) || freeCash < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną kwotę wolnej gotówki (≥ 0).";
                }
            }

            // Gotówka odłożona
            if (!string.IsNullOrWhiteSpace(EnvelopeCashBox.Text))
            {
                if (!decimal.TryParse(EnvelopeCashBox.Text, NumberStyles.Any, culture, out savedCash) || savedCash < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną kwotę gotówki odłożonej (≥ 0).";
                }
            }

            // Środki na głównym koncie bankowym
            if (!string.IsNullOrWhiteSpace(MainBankBox.Text))
            {
                if (!decimal.TryParse(MainBankBox.Text, NumberStyles.Any, culture, out bankTotal) || bankTotal < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną kwotę na głównym koncie bankowym (≥ 0).";
                }
            }

            if (!ok)
            {
                MessageBox.Show("Sprawdź wprowadzone wartości:" + errors,
                    "Nieprawidłowe dane",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return ok;
        }

        /// <summary>
        /// Zwraca nazwę wybranego banku z ComboBoxa (np. "mBank", "PKO Bank Polski").
        /// Dla pozycji "Wybierz bank" i "Inny bank" zwraca pusty string.
        /// </summary>
        private string GetSelectedMainBankName()
        {
            if (MainBankCombo.SelectedItem is ComboBoxItem item)
            {
                // 1) Prosty przypadek – sam string
                if (item.Content is string s)
                    return s == "Inny bank" || s == "Wybierz bank" ? "" : s;

                // 2) Nasz StackPanel: [Image][TextBlock]
                if (item.Content is StackPanel sp)
                {
                    foreach (var child in sp.Children)
                    {
                        if (child is TextBlock tb)
                        {
                            var text = tb.Text;
                            return text == "Inny bank" || text == "Wybierz bank" ? "" : text;
                        }
                    }
                }
            }

            return "";
        }

        private void SaveAndContinue_Click(object sender, RoutedEventArgs e)
        {
            if (_userId <= 0)
            {
                MessageBox.Show("Brak zalogowanego użytkownika.", "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (!TryReadValues(out var freeCash, out var savedCash, out var bankTotal))
                return;

            // 1) Gotówka i gotówka odłożona
            // CashOnHand stores TOTAL cash (wolna + odłożona), so save sum here.
            var totalCash = freeCash + savedCash;
            TransactionsFacadeService.SetCashOnHand(_userId, totalCash);
            TransactionsFacadeService.SetSavedCash(_userId, savedCash);

            // 2) Konto bankowe na start
            var mainBankName = GetSelectedMainBankName();

            // nazwa rachunku widoczna na kafelku:
            // - jeśli wybrano bank -> nazwa banku (mBank, PKO...),
            // - jeśli nie wybrano -> "Rachunek startowy"
            var accountName = string.IsNullOrWhiteSpace(mainBankName)
                ? "Rachunek startowy"
                : mainBankName;

            if (bankTotal > 0)
            {
                var acc = new BankAccountModel
                {
                    UserId = _userId,
                    BankName = string.IsNullOrWhiteSpace(mainBankName) ? "Konto bankowe" : mainBankName,
                    AccountName = accountName,
                    Iban = "",
                    Currency = "PLN",
                    Balance = bankTotal
                };

                DatabaseService.InsertAccount(acc);
            }

            // 3) Oznaczamy użytkownika jako „po konfiguracji”
            DatabaseService.MarkUserOnboarded(_userId);

            var shell = new ShellWindow();
            Application.Current.MainWindow = shell;
            shell.Show();
            Close();
        }
    }
}









