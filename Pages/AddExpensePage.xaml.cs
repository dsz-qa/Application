using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class AddExpensePage : UserControl
    {
        private readonly int _uid;

        // Specjalna pozycja „Gotówka (portfel)” dla transferów
        private const string CashKey = "__CASH__";

        private sealed class AccountItem
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public int? AccountId { get; set; } // null = gotówka

            public override string ToString() => Name;
        }

        // Bezparametrowy konstruktor – dla XAML / prostszych wywołań
        public AddExpensePage() : this(UserService.GetCurrentUserId())
        {
        }

        public AddExpensePage(int userId)
        {
            InitializeComponent();

            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            Loaded += (_, __) =>
            {
                // na start: żadna zakładka nie wybrana, wszystko ukryte
                ModeTabs.SelectedIndex = -1;
                ExpensePanel.Visibility = Visibility.Collapsed;
                IncomePanel.Visibility = Visibility.Collapsed;
                TransferPanel.Visibility = Visibility.Collapsed;

                LoadCategories();
                LoadTransferAccounts();

                // Domyślne daty: dziś
                ExpenseDatePicker.SelectedDate = DateTime.Today;
                IncomeDatePicker.SelectedDate = DateTime.Today;
                TransferDatePicker.SelectedDate = DateTime.Today;
            };
        }

        // ================== PRZEŁĄCZANIE ZAKŁADEK ==================

        private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeTabs.SelectedItem is not TabItem tab)
            {
                ExpensePanel.Visibility = Visibility.Collapsed;
                IncomePanel.Visibility = Visibility.Collapsed;
                TransferPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var header = tab.Header as string ?? string.Empty;

            ExpensePanel.Visibility =
                header.Equals("Wydatek", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;

            IncomePanel.Visibility =
                header.Equals("Przychód", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;

            TransferPanel.Visibility =
                header.Equals("Transfer", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================== DANE SŁOWNIKOWE ==================

        /// <summary>
        /// Ładuje listę kategorii użytkownika do trzech comboboxów:
        /// wydatek, przychód, transfer.
        /// </summary>
        private void LoadCategories()
        {
            try
            {
                var cats = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();

                if (ExpenseCategoryBox != null)
                    ExpenseCategoryBox.ItemsSource = cats;

                if (IncomeCategoryBox != null)
                    IncomeCategoryBox.ItemsSource = cats;

                if (TransferCategoryBox != null)
                    TransferCategoryBox.ItemsSource = cats;

                if (ExpenseCategoryBox != null && ExpenseCategoryBox.Items.Count > 0)
                    ExpenseCategoryBox.SelectedIndex = 0;
            }
            catch
            {
                if (ExpenseCategoryBox != null) ExpenseCategoryBox.ItemsSource = Array.Empty<string>();
                if (IncomeCategoryBox != null) IncomeCategoryBox.ItemsSource = Array.Empty<string>();
                if (TransferCategoryBox != null) TransferCategoryBox.ItemsSource = Array.Empty<string>();
            }
        }

        private void LoadTransferAccounts()
        {
            var items = new List<AccountItem>
            {
                new AccountItem
                {
                    Key = CashKey,
                    Name = "Gotówka (portfel)",
                    AccountId = null
                }
            };

            try
            {
                var accs = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();

                items.AddRange(
                    accs.Select(a => new AccountItem
                    {
                        Key = $"acc:{a.Id}",
                        AccountId = a.Id,
                        Name = $"{a.AccountName} — {a.Balance.ToString("N2", CultureInfo.CurrentCulture)} zł"
                    }));
            }
            catch
            {
                // zostawiamy tylko gotówkę
            }

            if (TransferFromBox != null)
            {
                TransferFromBox.ItemsSource = items.ToList();
                if (TransferFromBox.Items.Count > 0)
                    TransferFromBox.SelectedIndex = 0;
            }

            if (TransferToBox != null)
            {
                TransferToBox.ItemsSource = items.ToList();
                if (TransferToBox.Items.Count > 1)
                    TransferToBox.SelectedIndex = 1;
            }
        }

        // ================== ANULUJ / CZYSZCZENIE ==================

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (IsTab(sender, "Wydatek"))
            {
                ExpenseAmountBox.Clear();
                ExpenseDescBox.Clear();
                ExpenseDatePicker.SelectedDate = DateTime.Today;

                if (ExpenseCategoryBox != null)
                {
                    ExpenseCategoryBox.Text = string.Empty;
                    if (ExpenseCategoryBox.Items.Count > 0)
                        ExpenseCategoryBox.SelectedIndex = 0;
                }
            }
            else if (IsTab(sender, "Przychód"))
            {
                IncomeAmountBox.Clear();
                IncomeSourceBox.Clear();
                IncomeDescBox.Clear();
                IncomeDatePicker.SelectedDate = DateTime.Today;

                IncomeFormTypeCombo.SelectedIndex = -1;
                IncomeAccountRow.Visibility = Visibility.Collapsed;
                IncomeAccountCombo.SelectedIndex = -1;

                if (IncomeCategoryBox != null)
                {
                    IncomeCategoryBox.Text = string.Empty;
                    IncomeCategoryBox.SelectedIndex = -1;
                }
            }
            else // Transfer
            {
                TransferAmountBox.Clear();
                TransferDatePicker.SelectedDate = DateTime.Today;
                LoadTransferAccounts();

                if (TransferCategoryBox != null)
                {
                    TransferCategoryBox.Text = string.Empty;
                    TransferCategoryBox.SelectedIndex = -1;
                }
            }
        }

        private bool IsTab(object sender, string headerText)
        {
            var btn = sender as DependencyObject;
            var tabItem = FindParent<TabItem>(btn);

            return (tabItem?.Header as string)?
                .Equals(headerText, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
                if (parent is T t) return t;
                child = parent;
            }
            return null;
        }

        // ================== FORMULARZ PRZYCHODU – FORMA I KONTO ==================

        private void IncomeFormTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IncomeFormTypeCombo.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag as string ?? string.Empty;

                if (tag == "transfer")
                {
                    IncomeAccountRow.Visibility = Visibility.Visible;
                }
                else
                {
                    IncomeAccountRow.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                IncomeAccountRow.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Rejestruje kategorię z edytowalnego ComboBoxa w tabeli kategorii (jeśli jest nowa).
        /// </summary>
        private void RegisterCategoryFromCombo(ComboBox? combo)
        {
            if (combo == null) return;

            string? catName = null;

            if (!string.IsNullOrWhiteSpace(combo.Text))
            {
                catName = combo.Text.Trim();
            }
            else if (combo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
            {
                catName = s;
            }

            if (!string.IsNullOrWhiteSpace(catName))
            {
                try
                {
                    _ = DatabaseService.GetOrCreateCategoryId(_uid, catName!);
                }
                catch
                {
                    // nic, najwyżej kategoria się nie dopisze
                }
            }
        }

        // ================== WYDATEK ==================

        private void SaveExpense_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseAmount(ExpenseAmountBox.Text, out var amount))
            {
                MessageBox.Show("Podaj poprawną kwotę wydatku.", "Dodaj wydatek",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var date = ExpenseDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(ExpenseDescBox.Text)
                ? null
                : ExpenseDescBox.Text.Trim();

            // --- kategoria: istniejąca albo nowo wpisana ---
            string? catName = null;

            if (!string.IsNullOrWhiteSpace(ExpenseCategoryBox.Text))
            {
                catName = ExpenseCategoryBox.Text.Trim();
            }
            else if (ExpenseCategoryBox.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
            {
                catName = s;
            }

            int categoryId = 0;
            if (!string.IsNullOrWhiteSpace(catName))
            {
                try
                {
                    categoryId = DatabaseService.GetOrCreateCategoryId(_uid, catName!);
                }
                catch
                {
                    categoryId = 0;
                }
            }

            var eModel = new Expense
            {
                UserId = _uid,
                Amount = (double)amount,
                Date = date,
                Description = desc,
                CategoryId = categoryId
            };

            try
            {
                DatabaseService.InsertExpense(eModel);
                ToastService.Success("Dodano wydatek.");

                // przeładuj listę kategorii, żeby nowa się pojawiła
                LoadCategories();

                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nie udało się dodać wydatku.\n" + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================== PRZYCHÓD ==================

        private void SaveIncome_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseAmount(IncomeAmountBox.Text, out var amount))
            {
                MessageBox.Show("Podaj poprawną kwotę przychodu.", "Dodaj przychód",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var date = IncomeDatePicker.SelectedDate ?? DateTime.Today;
            var source = string.IsNullOrWhiteSpace(IncomeSourceBox.Text)
                ? "Przychód"
                : IncomeSourceBox.Text.Trim();
            var desc = string.IsNullOrWhiteSpace(IncomeDescBox.Text)
                ? null
                : IncomeDescBox.Text.Trim();

            // rejestrujemy kategorię (dla list rozwijanych)
            RegisterCategoryFromCombo(IncomeCategoryBox);

            try
            {
                DatabaseService.InsertIncome(_uid, amount, date, source, desc);
                ToastService.Success("Dodano przychód.");

                // odśwież listę kategorii (gdyby była nowa)
                LoadCategories();

                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nie udało się dodać przychodu.\n" + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================== TRANSFER ==================

        private void SaveTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseAmount(TransferAmountBox.Text, out var amount) || amount <= 0m)
            {
                MessageBox.Show("Podaj poprawną kwotę transferu.", "Transfer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var from = TransferFromBox.SelectedItem as AccountItem;
            var to = TransferToBox.SelectedItem as AccountItem;

            if (from == null || to == null)
            {
                MessageBox.Show("Wybierz konta źródłowe i docelowe.", "Transfer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (from.Key == to.Key)
            {
                MessageBox.Show("Konta źródłowe i docelowe nie mogą być takie same.",
                    "Transfer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // rejestrujemy kategorię transferu w tabeli kategorii
            RegisterCategoryFromCombo(TransferCategoryBox);

            try
            {
                if (from.AccountId is int accFrom && to.AccountId is null)
                {
                    // Bank -> gotówka
                    DatabaseService.TransferBankToCash(_uid, accFrom, amount);
                }
                else if (from.AccountId is null && to.AccountId is int accTo)
                {
                    // Gotówka -> bank
                    DatabaseService.TransferCashToBank(_uid, accTo, amount);
                }
                else
                {
                    MessageBox.Show(
                        "Na razie obsługiwane są transfery tylko między kontem bankowym a gotówką.",
                        "Transfer", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ToastService.Success("Zapisano transfer.");
                Cancel_Click(sender, e);
                LoadTransferAccounts();   // odśwież salda
                LoadCategories();         // odśwież kategorie, jeśli doszła nowa
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nie udało się zapisać transferu.\n" + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================== WALIDACJA ==================

        private static bool TryParseAmount(string? s, out decimal value)
        {
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("pl-PL"), out value)) return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            return false;
        }
    }
}
















