using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class AddExpensePage : UserControl
    {
        private readonly int _uid;

        private enum TransferItemKind
        {
            FreeCash,      // wolna gotówka
            SavedCash,     // odłożona gotówka
            BankAccount,   // konto bankowe
            Envelope       // koperta
        }

        private sealed class TransferItem
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public TransferItemKind Kind { get; set; }
            public int? BankAccountId { get; set; }
            public int? EnvelopeId { get; set; }

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
                // KPI – tak jak na Dashboardzie
                RefreshMoneySummary();

                // start – żadna zakładka nie wybrana
                ModeTabs.SelectedIndex = -1;
                ShowPanels(null);

                LoadCategories();
                LoadTransferItems();
                LoadEnvelopes();
                LoadIncomeAccounts();

                // Domyślne daty
                var today = DateTime.Today;
                ExpenseDatePicker.SelectedDate = today;
                IncomeDatePicker.SelectedDate = today;
                TransferDatePicker.SelectedDate = today;
            };
        }

        // ================== KPI (jak na DashboardPage) ==================

        private void SetKpiText(string name, decimal value)
        {
            if (FindName(name) is TextBlock tb)
                tb.Text = value.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void RefreshMoneySummary()
        {
            if (_uid <= 0) return;

            var snap = DatabaseService.GetMoneySnapshot(_uid);

            SetKpiText("TotalWealthText", snap.Total);
            SetKpiText("BanksText", snap.Banks);
            SetKpiText("FreeCashDashboardText", snap.Cash);
            SetKpiText("SavedToAllocateText", snap.SavedUnallocated);
            SetKpiText("EnvelopesDashboardText", snap.Envelopes);
            SetKpiText("InvestmentsText", 0m);
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

                // Przychody – dla przelewów
                IncomeAccountCombo.ItemsSource = accs;
                IncomeAccountCombo.DisplayMemberPath = "AccountName";
                IncomeAccountCombo.SelectedValuePath = "Id";

                // Wydatki – gdy źródło to „Konto bankowe”
                ExpenseBankAccountCombo.ItemsSource = accs.ToList();
                ExpenseBankAccountCombo.DisplayMemberPath = "AccountName";
                ExpenseBankAccountCombo.SelectedValuePath = "Id";
            }
            catch
            {
                IncomeAccountCombo.ItemsSource = null;
                ExpenseBankAccountCombo.ItemsSource = null;
            }
        }

        /// <summary>
        /// Buduje listę wszystkich miejsc, między którymi można robić transfer:
        /// - wolna gotówka
        /// - odłożona gotówka
        /// - każde konto bankowe
        /// - każda koperta
        /// </summary>
        private void LoadTransferItems()
        {
            var items = new List<TransferItem>
            {
                new TransferItem
                {
                    Key  = "free",
                    Name = "Wolna gotówka",
                    Kind = TransferItemKind.FreeCash
                },
                new TransferItem
                {
                    Key  = "saved",
                    Name = "Odłożona gotówka",
                    Kind = TransferItemKind.SavedCash
                }
            };

            // Konta bankowe
            try
            {
                var accs = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();

                items.AddRange(
                    accs.Select(a => new TransferItem
                    {
                        Key = $"bank:{a.Id}",
                        Kind = TransferItemKind.BankAccount,
                        BankAccountId = a.Id,
                        Name = $"Konto bankowe: {a.AccountName} — {a.Balance.ToString("N2", CultureInfo.CurrentCulture)} zł"
                    }));
            }
            catch
            {
                // pomijamy konta jeśli coś pójdzie nie tak
            }

            // Koperty
            try
            {
                var dt = DatabaseService.GetEnvelopesTable(_uid);
                if (dt != null)
                {
                    foreach (DataRow r in dt.Rows)
                    {
                        var id = Convert.ToInt32(r["Id"]);
                        var name = r["Name"]?.ToString() ?? "(bez nazwy)";
                        decimal allocated = 0m;
                        if (r["Allocated"] != DBNull.Value)
                            allocated = Convert.ToDecimal(r["Allocated"]);

                        items.Add(new TransferItem
                        {
                            Key = $"env:{id}",
                            Kind = TransferItemKind.Envelope,
                            EnvelopeId = id,
                            Name = $"Koperta: {name} — {allocated.ToString("N2", CultureInfo.CurrentCulture)} zł"
                        });
                    }
                }
            }
            catch
            {
                // brak kopert – trudno
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
            ExpenseBankRow.Visibility = Visibility.Collapsed;
            ExpenseBankAccountCombo.SelectedIndex = -1;

            IncomeFormTypeCombo.SelectedIndex = -1;
            IncomeAccountRow.Visibility = Visibility.Collapsed;
            IncomeAccountCombo.SelectedIndex = -1;

            ExpenseCategoryBox.SelectedIndex = -1;
            IncomeCategoryBox.SelectedIndex = -1;
            TransferCategoryBox.SelectedIndex = -1;

            ExpenseEnvelopeCombo.SelectedIndex = -1;

            LoadTransferItems();
            LoadCategories();
            LoadEnvelopes();
            LoadIncomeAccounts();
        }

        // ================== OBSŁUGA FORM PRZYCHODU / SKĄD PŁACISZ ==================

        private void ExpenseSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ExpenseEnvelopeRow.Visibility = Visibility.Collapsed;
            ExpenseBankRow.Visibility = Visibility.Collapsed;

            if (ExpenseSourceCombo.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag as string;

                if (string.Equals(tag, "envelope", StringComparison.OrdinalIgnoreCase))
                {
                    ExpenseEnvelopeRow.Visibility = Visibility.Visible;
                }
                else if (string.Equals(tag, "bank", StringComparison.OrdinalIgnoreCase))
                {
                    ExpenseBankRow.Visibility = Visibility.Visible;
                }
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

            if (ExpenseSourceCombo.SelectedItem is not ComboBoxItem sourceItem)
            {
                ToastService.Info("Wybierz skąd płacisz.");
                return;
            }

            var sourceTag = sourceItem.Tag as string ?? "";
            var date = ExpenseDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(ExpenseDescBox.Text)
                ? null
                : ExpenseDescBox.Text.Trim();

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
                // 1) Odejmujemy z właściwego źródła
                switch (sourceTag)
                {
                    case "cash_free":
                        DatabaseService.SpendFromFreeCash(_uid, amount);
                        break;

                    case "cash_saved":
                        DatabaseService.SpendFromSavedCash(_uid, amount);
                        break;

                    case "envelope":
                        if (ExpenseEnvelopeCombo.SelectedItem is not string envName ||
                            string.IsNullOrWhiteSpace(envName))
                        {
                            ToastService.Info("Wybierz kopertę, z której chcesz zapłacić.");
                            return;
                        }

                        var envId = DatabaseService.GetEnvelopeIdByName(_uid, envName);
                        if (envId == null)
                        {
                            ToastService.Error("Nie udało się odnaleźć wybranej koperty.");
                            return;
                        }

                        DatabaseService.SpendFromEnvelope(_uid, envId.Value, amount);
                        break;

                    case "bank":
                        if (ExpenseBankAccountCombo.SelectedItem is not BankAccountModel acc)
                        {
                            ToastService.Info("Wybierz konto bankowe, z którego ma zejść wydatek.");
                            return;
                        }

                        DatabaseService.SpendFromBankAccount(_uid, acc.Id, amount);
                        break;

                    default:
                        ToastService.Info("Wybierz poprawne źródło płatności.");
                        return;
                }

                // 2) Zapisujemy wydatek
                DatabaseService.InsertExpense(eModel);
                ToastService.Success("Dodano wydatek.");

                LoadCategories();
                LoadEnvelopes();
                LoadIncomeAccounts();

                // odśwież KPI
                RefreshMoneySummary();

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

            if (IncomeFormTypeCombo.SelectedItem is not ComboBoxItem formItem)
            {
                ToastService.Info("Wybierz formę przychodu.");
                return;
            }

            var formTag = formItem.Tag as string ?? "";
            var date = IncomeDatePicker.SelectedDate ?? DateTime.Today;
            var source = string.IsNullOrWhiteSpace(IncomeSourceBox.Text)
                ? "Przychód"
                : IncomeSourceBox.Text.Trim();
            var desc = string.IsNullOrWhiteSpace(IncomeDescBox.Text)
                ? null
                : IncomeDescBox.Text.Trim();

            var catName = ResolveCategoryName(IncomeCategoryBox, IncomeNewCategoryBox);
            int? categoryId = GetCategoryIdOrZero(catName);
            if (categoryId == 0) categoryId = null;

            try
            {
                switch (formTag)
                {
                    case "cash_free":
                        DatabaseService.AddIncomeToFreeCash(_uid, amount, date, categoryId, source, desc);
                        break;

                    case "cash_saved":
                        DatabaseService.AddIncomeToSavedCash(_uid, amount, date, categoryId, source, desc);
                        break;

                    case "transfer":
                        if (IncomeAccountCombo.SelectedItem is not BankAccountModel acc)
                        {
                            ToastService.Info("Wybierz konto bankowe, na które wpływa przelew.");
                            return;
                        }
                        DatabaseService.AddIncomeToBankAccount(_uid, acc.Id, amount, date, categoryId, source, desc);
                        break;

                    default:
                        ToastService.Info("Wybierz poprawną formę przychodu.");
                        return;
                }

                ToastService.Success("Dodano przychód.");

                LoadCategories();
                LoadIncomeAccounts();

                // odśwież KPI
                RefreshMoneySummary();

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

            var from = TransferFromBox.SelectedItem as TransferItem;
            var to = TransferToBox.SelectedItem as TransferItem;

            if (from == null || to == null || from.Key == to.Key)
            {
                ToastService.Info("Wybierz różne konta źródłowe i docelowe.");
                return;
            }

            var catName = ResolveCategoryName(TransferCategoryBox, TransferNewCategoryBox);
            _ = GetCategoryIdOrZero(catName); // na razie kategoria tylko informacyjnie

            try
            {
                bool handled = false;

                // BANK <-> BANK
                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.BankAccount &&
                    from.BankAccountId is int accFrom &&
                    to.BankAccountId is int accTo)
                {
                    DatabaseService.TransferBankToBank(_uid, accFrom, accTo, amount);
                    handled = true;
                }

                // BANK -> WOLNA GOTÓWKA
                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.FreeCash &&
                    from.BankAccountId is int accFromBankToFree)
                {
                    DatabaseService.TransferBankToCash(_uid, accFromBankToFree, amount);
                    handled = true;
                }

                // WOLNA GOTÓWKA -> BANK
                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.BankAccount &&
                    to.BankAccountId is int accToFreeToBank)
                {
                    DatabaseService.TransferCashToBank(_uid, accToFreeToBank, amount);
                    handled = true;
                }

                // WOLNA GOTÓWKA -> ODŁOŻONA
                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.SavedCash)
                {
                    DatabaseService.TransferFreeToSaved(_uid, amount);
                    handled = true;
                }

                // ODŁOŻONA -> WOLNA
                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.FreeCash)
                {
                    DatabaseService.TransferSavedToFree(_uid, amount);
                    handled = true;
                }

                // ODŁOŻONA -> BANK
                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.BankAccount &&
                    to.BankAccountId is int accToSavedToBank)
                {
                    DatabaseService.TransferSavedToBank(_uid, accToSavedToBank, amount);
                    handled = true;
                }

                // BANK -> ODŁOŻONA
                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.SavedCash &&
                    from.BankAccountId is int accFromBankToSaved)
                {
                    DatabaseService.TransferBankToSaved(_uid, accFromBankToSaved, amount);
                    handled = true;
                }

                // KOPERTA -> KOPERTA
                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.Envelope &&
                    from.EnvelopeId is int envFrom &&
                    to.EnvelopeId is int envTo)
                {
                    DatabaseService.TransferEnvelopeToEnvelope(_uid, envFrom, envTo, amount);
                    handled = true;
                }

                // ODŁOŻONA -> KOPERTA
                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.Envelope &&
                    to.EnvelopeId is int envToFromSaved)
                {
                    DatabaseService.TransferSavedToEnvelope(_uid, envToFromSaved, amount);
                    handled = true;
                }

                // KOPERTA -> ODŁOŻONA
                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.SavedCash &&
                    from.EnvelopeId is int envFromToSaved)
                {
                    DatabaseService.TransferEnvelopeToSaved(_uid, envFromToSaved, amount);
                    handled = true;
                }

                // WOLNA GOTÓWKA -> KOPERTA
                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.Envelope &&
                    to.EnvelopeId is int envToFromFree)
                {
                    DatabaseService.TransferFreeToEnvelope(_uid, envToFromFree, amount);
                    handled = true;
                }

                // KOPERTA -> WOLNA GOTÓWKA
                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.FreeCash &&
                    from.EnvelopeId is int envFromToFree)
                {
                    DatabaseService.TransferEnvelopeToFree(_uid, envFromToFree, amount);
                    handled = true;
                }

                if (!handled)
                {
                    ToastService.Info("Ten rodzaj transferu nie jest jeszcze obsługiwany.");
                    return;
                }

                ToastService.Success("Zapisano transfer.");

                Cancel_Click(sender, e);
                LoadTransferItems();
                LoadCategories();

                // odśwież KPI
                RefreshMoneySummary();
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
