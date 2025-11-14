using Finly.Models;
using Finly.Services;
using System;
using System.Globalization;
using System.Windows;
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

        private bool TryReadValues(out decimal freeCash, out decimal savedCash, out decimal bankTotal)
        {
            // Żeby nie było CS0177 – ustawiamy wartości startowe:
            freeCash = 0m;
            savedCash = 0m;
            bankTotal = 0m;

            var culture = CultureInfo.CurrentCulture;

            bool ok = true;
            string errors = "";

            // Gotówka do dyspozycji
            if (!string.IsNullOrWhiteSpace(FreeCashBox.Text))
            {
                if (!decimal.TryParse(FreeCashBox.Text, NumberStyles.Any, culture, out freeCash) || freeCash < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną kwotę gotówki do dyspozycji (≥ 0).";
                }
            }

            // Gotówka odłożona
            if (!string.IsNullOrWhiteSpace(SavedCashBox.Text))
            {
                if (!decimal.TryParse(SavedCashBox.Text, NumberStyles.Any, culture, out savedCash) || savedCash < 0)
                {
                    ok = false;
                    errors += "\n• Podaj poprawną kwotę gotówki odłożonej (≥ 0).";
                }
            }

            // Środki na kontach bankowych
            if (!string.IsNullOrWhiteSpace(BankTotalBox.Text))
            {
                if (!decimal.TryParse(BankTotalBox.Text, NumberStyles.Any, culture, out bankTotal) || bankTotal < 0)
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

            // 1) Gotówka w portfelu + oszczędności w gotówce
            decimal totalCash = freeCash + savedCash;
            DatabaseService.SetCashOnHand(_userId, totalCash);

            // 2) Oszczędności w kopercie
            if (savedCash > 0)
            {
                DatabaseService.InsertEnvelope(
                    _userId,
                    "Oszczędności",
                    target: savedCash,
                    allocated: savedCash,
                    note: "Kwota startowa z pierwszej konfiguracji");
            }

            // 3) Konto bankowe
            if (bankTotal > 0)
            {
                var acc = new BankAccountModel
                {
                    UserId = _userId,
                    AccountName = "Konto startowe",
                    Iban = string.Empty,
                    Currency = "PLN",
                    Balance = bankTotal
                };

                DatabaseService.InsertAccount(acc);
            }

            // 🔹 kluczowe: oznaczamy jako skonfigurowanego
            UserService.MarkOnboarded(_userId);

            var shell = new ShellWindow();
            Application.Current.MainWindow = shell;
            shell.Show();
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            if (_userId > 0)
            {
                // nawet jeśli pominie konfigurację – traktujemy jako skonfigurowanego
                UserService.MarkOnboarded(_userId);
            }

            var shell = new ShellWindow();
            Application.Current.MainWindow = shell;
            shell.Show();
            Close();
        }

    }
}


