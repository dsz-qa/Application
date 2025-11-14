using Finly.Models;
using Finly.Services;
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


        // =================== Pasek tytułu ===================

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;
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

        /// <summary>
        /// Czyta wartości z pól tekstowych.
        /// Zawsze przypisuje coś do parametrów out, żeby nie było CS0177.
        /// Zwraca false, jeśli są błędy walidacji.
        /// </summary>
        private bool TryReadValues(out decimal freeCash, out decimal savedCash, out decimal bankTotal)
        {
            freeCash = 0m;
            savedCash = 0m;
            bankTotal = 0m;

            var culture = CultureInfo.CurrentCulture;
            bool ok = true;
            string errors = "";

            // Wolna gotówka (np. w portfelu)
            if (!string.IsNullOrWhiteSpace(FreeCashBox.Text))
            {
                if (!decimal.TryParse(FreeCashBox.Text, NumberStyles.Any, culture, out freeCash) || freeCash < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną kwotę wolnej gotówki (≥ 0).";
                }
            }

            // Gotówka odłożona (na koperty)
            if (!string.IsNullOrWhiteSpace(EnvelopeCashBox.Text))
            {
                if (!decimal.TryParse(EnvelopeCashBox.Text, NumberStyles.Any, culture, out savedCash) || savedCash < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną kwotę gotówki odłożonej (≥ 0).";
                }
            }

            // Środki na kontach bankowych
            if (!string.IsNullOrWhiteSpace(MainBankBox.Text))
            {
                if (!decimal.TryParse(MainBankBox.Text, NumberStyles.Any, culture, out bankTotal) || bankTotal < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną łączną kwotę na kontach bankowych (≥ 0).";
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

        private void SaveAndContinue_Click(object sender, RoutedEventArgs e)
        {
            if (_userId <= 0)
            {
                MessageBox.Show("Brak zalogowanego użytkownika.", "Błąd",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!TryReadValues(out var freeCash, out var savedCash, out var bankTotal))
                return;

            // 1) Zapisujemy:
            //    - CashOnHand = wolna gotówka (którą możesz wydawać)
            //    - SavedCash  = cała odłożona gotówka (pula pod koperty)
            DatabaseService.SetCashOnHand(_userId, freeCash);
            DatabaseService.SetSavedCash(_userId, savedCash);

            // 2) Jeśli użytkownik wpisał kwotę na kontach bankowych – tworzymy konto startowe
            if (bankTotal > 0)
            {
                var acc = new BankAccountModel
                {
                    UserId = _userId,
                    AccountName = "Rachunek startowy",
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


        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            if (_userId > 0)
            {
                DatabaseService.MarkUserOnboarded(_userId);
            }

            var shell = new ShellWindow();
            Application.Current.MainWindow = shell;
            shell.Show();
            Close();
        }
    }
}



