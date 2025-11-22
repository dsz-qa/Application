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

        public AddExpensePage() : this(UserService.GetCurrentUserId())
        {
        }

        public AddExpensePage(int userId)
        {
            InitializeComponent();

            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            Loaded += (_, __) =>
            {
                // start – żadna zakładka nie wybrana
                ModeTabs.SelectedIndex = -1;
                ShowPanels(null);

                LoadCategories();
                LoadTransferAccounts();
                LoadEnvelopes();
                LoadIncomeAccounts();

                // Domyślne daty
                var today = DateTime.Today;
                ExpenseDatePicker.SelectedDate = today;
                IncomeDatePicker.SelectedDate = today;
                TransferDatePicker.SelectedDate = today;
            };
        }

        // ================== PANELE / ZAKŁADKI ==================

        private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tab = ModeTabs.SelectedItem as TabItem;
            var header = tab?.Header as string;

            ShowPanels(header);
            ClearAmountErrors();
        }

        private void ShowPanels(string? header)
        {
            ExpensePanel.Visibility =
                string.Equals(header, "Wydatek", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;

            IncomePanel.Visibility =
                string.Equals(header, "Przychód", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;

            TransferPanel.Visibility =
                string.Equals(header, "Transfer", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================== DANE SŁOWNIKOWE ==================

        private void LoadCategories()
        {
            List<string> cats;
            try
            {
                cats = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();
            }
            catch
            {
                cats = new List<string>();
            }

            ExpenseCategoryBox.ItemsSource = cats;
            IncomeCategoryBox.ItemsSource = cats;
            TransferCategoryBox.ItemsSource = cats;
        }

        private void LoadEnvelopes()
        {
            try
            {
                var envs = DatabaseService.GetEnvelopesNames(_uid) ?? new List<string>();
                ExpenseEnvelopeCombo.ItemsSource = envs;
                if (envs.Count > 0)
                    ExpenseEnvelopeCombo.SelectedIndex = 0;
            }
            catch
            {
                ExpenseEnvelopeCombo.ItemsSource = Array.Empty<string>();
            }
        }

        private void LoadIncomeAccounts()
        {
            try
            {
                var accs = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();
                IncomeAccountCombo.ItemsSource = accs;
                IncomeAccountCombo.DisplayMemberPath = "AccountName";
                IncomeAccountCombo.SelectedValuePath = "Id";
            }
            catch
            {
                IncomeAccountCombo.ItemsSource = null;
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

            TransferFromBox.ItemsSource = items.ToList();
            TransferToBox.ItemsSource = items.ToList();

            if (TransferFromBox.Items.Count > 0)
                TransferFromBox.SelectedIndex = 0;
            if (TransferToBox.Items.Count > 1)
                TransferToBox.SelectedIndex = 1;
        }

        // ================== ANULUJ / CZYSZCZENIE ==================

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ClearAmountErrors();

            // Wspólne
            ExpenseAmountBox.Clear();
            IncomeAmountBox.Clear();
            TransferAmountBox.Clear();

            ExpenseDescBox.Clear();
            IncomeDescBox.Clear();
            TransferDescBox.Clear();

            ExpenseNewCategoryBox.Clear();
            IncomeNewCategoryBox.Clear();
            TransferNewCategoryBox.Clear();

            ExpenseDatePicker.SelectedDate = DateTime.Today;
            IncomeDatePicker.SelectedDate = DateTime.Today;
            TransferDatePicker.SelectedDate = DateTime.Today;

            ExpenseSourceCombo.SelectedIndex = -1;
            ExpenseEnvelopeRow.Visibility = Visibility.Collapsed;

            IncomeFormTypeCombo.SelectedIndex = -1;
            IncomeAccountRow.Visibility = Visibility.Collapsed;
            IncomeAccountCombo.SelectedIndex = -1;

            ExpenseCategoryBox.SelectedIndex = -1;
            IncomeCategoryBox.SelectedIndex = -1;
            TransferCategoryBox.SelectedIndex = -1;

            ExpenseEnvelopeCombo.SelectedIndex = -1;

            LoadTransferAccounts();
            LoadCategories();
            LoadEnvelopes();
            LoadIncomeAccounts();
        }

        // ================== OBSŁUGA FORM PRZYCHODU / SKĄD PŁACISZ ==================

        private void ExpenseSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExpenseSourceCombo.SelectedItem is ComboBoxItem item &&
                string.Equals(item.Tag as string, "envelope", StringComparison.OrdinalIgnoreCase))
            {
                ExpenseEnvelopeRow.Visibility = Visibility.Visible;
            }
            else
            {
                ExpenseEnvelopeRow.Visibility = Visibility.Collapsed;
            }
        }

        private void IncomeFormTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IncomeFormTypeCombo.SelectedItem is ComboBoxItem item &&
                string.Equals(item.Tag as string, "transfer", StringComparison.OrdinalIgnoreCase))
            {
                IncomeAccountRow.Visibility = Visibility.Visible;
            }
            else
            {
                IncomeAccountRow.Visibility = Visibility.Collapsed;
            }
        }

        // ================== POMOCNICZE ==================

        private void ClearAmountErrors()
        {
            ExpenseAmountErrorText.Visibility = Visibility.Collapsed;
            IncomeAmountErrorText.Visibility = Visibility.Collapsed;
            TransferAmountErrorText.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Wybiera nazwę kategorii z listy + pola "Nowa kategoria".
        /// Jeśli nowa kategoria nie jest pusta – ma priorytet.
        /// </summary>
        private string? ResolveCategoryName(ComboBox combo, TextBox newCatBox)
        {
            if (!string.IsNullOrWhiteSpace(newCatBox.Text))
                return newCatBox.Text.Trim();

            if (combo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
                return s.Trim();

            return null;
        }

        private int GetCategoryIdOrZero(string? catName)
        {
            if (string.IsNullOrWhiteSpace(catName)) return 0;

            try
            {
                return DatabaseService.GetOrCreateCategoryId(_uid, catName);
            }
            catch
            {
                return 0;
            }
        }

        // ================== WYDATEK ==================

        private void SaveExpense_Click(object sender, RoutedEventArgs e)
        {
            ClearAmountErrors();

            if (!TryParseAmount(ExpenseAmountBox.Text, out var amount) || amount <= 0m)
            {
                ExpenseAmountErrorText.Visibility = Visibility.Visible;
                return;
            }

            var date = ExpenseDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(ExpenseDescBox.Text)
                ? null
                : ExpenseDescBox.Text.Trim();

            // kategoria
            var catName = ResolveCategoryName(ExpenseCategoryBox, ExpenseNewCategoryBox);
            int categoryId = GetCategoryIdOrZero(catName);

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

                // odśwież kategorie (nowa może się pojawić na liście)
                LoadCategories();
                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać wydatku.\n" + ex.Message);
            }
        }

        // ================== PRZYCHÓD ==================

        private void SaveIncome_Click(object sender, RoutedEventArgs e)
        {
            ClearAmountErrors();

            if (!TryParseAmount(IncomeAmountBox.Text, out var amount) || amount <= 0m)
            {
                IncomeAmountErrorText.Visibility = Visibility.Visible;
                return;
            }

            var date = IncomeDatePicker.SelectedDate ?? DateTime.Today;
            var source = string.IsNullOrWhiteSpace(IncomeSourceBox.Text)
                ? "Przychód"
                : IncomeSourceBox.Text.Trim();
            var desc = string.IsNullOrWhiteSpace(IncomeDescBox.Text)
                ? null
                : IncomeDescBox.Text.Trim();

            // kategoria – na razie tylko na potrzeby przyszłej rozbudowy
            var catName = ResolveCategoryName(IncomeCategoryBox, IncomeNewCategoryBox);
            _ = GetCategoryIdOrZero(catName); // żeby nie było niewykorzystanej zmiennej

            try
            {
                // aktualny DatabaseService.InsertIncome nie przyjmuje kategorii – zapisujemy tak jak do tej pory
                DatabaseService.InsertIncome(_uid, amount, date, source, desc);
                ToastService.Success("Dodano przychód.");

                LoadCategories();
                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać przychodu.\n" + ex.Message);
            }
        }

        // ================== TRANSFER ==================

        private void SaveTransfer_Click(object sender, RoutedEventArgs e)
        {
            ClearAmountErrors();

            if (!TryParseAmount(TransferAmountBox.Text, out var amount) || amount <= 0m)
            {
                TransferAmountErrorText.Visibility = Visibility.Visible;
                return;
            }

            var from = TransferFromBox.SelectedItem as AccountItem;
            var to = TransferToBox.SelectedItem as AccountItem;

            if (from == null || to == null || from.Key == to.Key)
            {
                ToastService.Info("Wybierz różne konta źródłowe i docelowe.");
                return;
            }

            // kategoria – na razie tylko wizualnie, bez zapisu
            var catName = ResolveCategoryName(TransferCategoryBox, TransferNewCategoryBox);
            _ = GetCategoryIdOrZero(catName);

            try
            {
                if (from.AccountId is int accFrom && to.AccountId is null)
                {
                    // bank -> gotówka
                    DatabaseService.TransferBankToCash(_uid, accFrom, amount);
                }
                else if (from.AccountId is null && to.AccountId is int accTo)
                {
                    // gotówka -> bank
                    DatabaseService.TransferCashToBank(_uid, accTo, amount);
                }
                else
                {
                    ToastService.Info("Na razie obsługiwane są transfery tylko między kontem bankowym a gotówką.");
                    return;
                }

                ToastService.Success("Zapisano transfer.");

                Cancel_Click(sender, e);
                LoadTransferAccounts();   // odśwież salda
                LoadCategories();         // odśwież kategorie
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać transferu.\n" + ex.Message);
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
