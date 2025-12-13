using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static Finly.Models.Expense;

namespace Finly.Pages
{
    public partial class AddExpensePage : UserControl
    {
        private readonly int _uid;
        private List<Budget> _budgets = new();

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

        // ================== BUDŻETY – POMOCNICZA METODA ==================

        /// <summary>
        /// Zwraca Id wybranego budżetu (albo null, jeśli nic nie wybrano
        /// albo data nie mieści się w zakresie budżetu).
        /// </summary>
        private int? GetSelectedBudgetId(System.Windows.Controls.ComboBox combo, DateTime date)
        {
            if (combo?.SelectedItem is Finly.Models.Budget b)
            {
                if (b.StartDate <= date && date <= b.EndDate)
                    return b.Id;

                ToastService.Info("Wybrany budżet nie obejmuje daty tej operacji – zostanie zapisana bez powiązania z budżetem.");
            }

            return null;
        }

        public AddExpensePage() : this(UserService.GetCurrentUserId())
        {
        }

        // ================== MISSING XAML EVENT HANDLERS (fix CS1061) ==================

        // Te metody są podpięte w AddExpensePage.xaml (różne eventy).
        // Robimy overloady, żeby XAML zawsze znalazł pasującą sygnaturę.

        private void ExpenseForm_Changed(object sender, RoutedEventArgs e) => UpdateExpensePlannedVisibility();
        private void ExpenseForm_Changed(object sender, SelectionChangedEventArgs e) => UpdateExpensePlannedVisibility();
        private void ExpenseForm_Changed(object sender, TextChangedEventArgs e) => UpdateExpensePlannedVisibility();

        private void IncomeForm_Changed(object sender, RoutedEventArgs e) => UpdateIncomePlannedVisibility();
        private void IncomeForm_Changed(object sender, SelectionChangedEventArgs e) => UpdateIncomePlannedVisibility();
        private void IncomeForm_Changed(object sender, TextChangedEventArgs e) => UpdateIncomePlannedVisibility();

        private void TransferForm_Changed(object sender, RoutedEventArgs e) => UpdateTransferPlannedVisibility();
        private void TransferForm_Changed(object sender, SelectionChangedEventArgs e) => UpdateTransferPlannedVisibility();
        private void TransferForm_Changed(object sender, TextChangedEventArgs e) => UpdateTransferPlannedVisibility();

        public AddExpensePage(int userId)
        {
            InitializeComponent();

            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            Loaded += (_, __) =>
            {
                RefreshMoneySummary();

                ModeTabs.SelectedIndex = -1;
                ShowPanels(null);

                LoadCategories();
                LoadTransferItems();
                LoadEnvelopes();
                LoadIncomeAccounts();
                LoadBudgets();

                var today = DateTime.Today;
                ExpenseDatePicker.SelectedDate = today;
                IncomeDatePicker.SelectedDate = today;
                TransferDatePicker.SelectedDate = today;

                if (FindName("ExpensePlannedDatePicker") is DatePicker ep) ep.SelectedDate = today;
                if (FindName("IncomePlannedDatePicker") is DatePicker ip) ip.SelectedDate = today;
                if (FindName("TransferPlannedDatePicker") is DatePicker tp) tp.SelectedDate = today;

                // copy lists into planned combos
                ExpensePlannedCategoryBox.ItemsSource = ExpenseCategoryBox.ItemsSource;
                IncomePlannedCategoryBox.ItemsSource = IncomeCategoryBox.ItemsSource;

                // populate income planned source/account
                if (IncomePlannedAccountCombo != null)
                {
                    IncomePlannedAccountCombo.ItemsSource =
                        DatabaseService.GetAccounts(_uid)?.Select(a => a.AccountName).ToList()
                        ?? new List<string>();
                }

                // populate transfer planned accounts
                TransferPlannedFromBox.ItemsSource = TransferFromBox.ItemsSource;
                TransferPlannedToBox.ItemsSource = TransferToBox.ItemsSource;

                // planned expense source handler
                if (ExpensePlannedSourceCombo != null)
                    ExpensePlannedSourceCombo.SelectionChanged += ExpensePlannedSourceCombo_SelectionChanged;

                // populate planned envelopes and bank accounts
                if (ExpensePlannedEnvelopeCombo != null)
                {
                    var envNames = DatabaseService.GetEnvelopesNames(_uid) ?? new List<string>();
                    ExpensePlannedEnvelopeCombo.ItemsSource = envNames;
                    if (envNames.Count > 0)
                        ExpensePlannedEnvelopeCombo.SelectedIndex = 0;
                }

                if (ExpensePlannedBankAccountCombo != null)
                {
                    var accs = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();
                    ExpensePlannedBankAccountCombo.ItemsSource = accs;
                    ExpensePlannedBankAccountCombo.DisplayMemberPath = "AccountName";
                    ExpensePlannedBankAccountCombo.SelectedValuePath = "Id";
                    if (accs.Count > 0) ExpensePlannedBankAccountCombo.SelectedIndex = 0;
                }

                // enable/disable planned save buttons based on amount
                ExpensePlannedAmountBox.TextChanged += (_, __2) =>
                    ExpensePlannedButton.IsEnabled = TryParseAmount(ExpensePlannedAmountBox.Text, out var a) && a > 0m;
                IncomePlannedAmountBox.TextChanged += (_, __2) =>
                    IncomePlannedButton.IsEnabled = TryParseAmount(IncomePlannedAmountBox.Text, out var b) && b > 0m;
                TransferPlannedAmountBox.TextChanged += (_, __2) =>
                    TransferPlannedButton.IsEnabled = TryParseAmount(TransferPlannedAmountBox.Text, out var c) && c > 0m;

                // planned income source handler
                if (IncomePlannedSourceCombo != null)
                    IncomePlannedSourceCombo.SelectionChanged += IncomePlannedSourceCombo_SelectionChanged;

                // wczytaj budżety pod bieżące daty (bez zależności od metody GetBudgetsForDate)
                LoadExpenseBudgetsForDate(ExpenseDatePicker.SelectedDate ?? DateTime.Today);
                LoadIncomeBudgetsForDate(IncomeDatePicker.SelectedDate ?? DateTime.Today);
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

            UpdateExpensePlannedVisibility();
            UpdateIncomePlannedVisibility();
            UpdateTransferPlannedVisibility();
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

        private void LoadTransferItems()
        {
            var items = new List<TransferItem>
            {
                new TransferItem { Key="free",  Name="Wolna gotówka",    Kind=TransferItemKind.FreeCash },
                new TransferItem { Key="saved", Name="Odłożona gotówka", Kind=TransferItemKind.SavedCash }
            };

            // Konta bankowe
            try
            {
                var accs = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();

                items.AddRange(accs.Select(a => new TransferItem
                {
                    Key = $"bank:{a.Id}",
                    Kind = TransferItemKind.BankAccount,
                    BankAccountId = a.Id,
                    Name = $"Konto bankowe: {a.AccountName} — {a.Balance.ToString("N2", CultureInfo.CurrentCulture)} zł"
                }));
            }
            catch { }

            // Koperty
            try
            {
                var dt = DatabaseService.GetEnvelopesTable(_uid);
                if (dt != null)
                {
                    foreach (DataRow r in dt.Rows)
                    {
                        var id = Convert.ToInt32(r["Id"]);
                        var name = (r["Name"]?.ToString() ?? "(bez nazwy)").Trim();
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
            catch { }

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

        // ================== OBSŁUGA FORM ==================

        private void ExpenseSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ExpenseEnvelopeRow.Visibility = Visibility.Collapsed;
            ExpenseBankRow.Visibility = Visibility.Collapsed;

            if (ExpenseSourceCombo.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag as string;

                if (string.Equals(tag, "envelope", StringComparison.OrdinalIgnoreCase))
                    ExpenseEnvelopeRow.Visibility = Visibility.Visible;
                else if (string.Equals(tag, "bank", StringComparison.OrdinalIgnoreCase))
                    ExpenseBankRow.Visibility = Visibility.Visible;
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

        private void IncomePlannedSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IncomePlannedSourceCombo.SelectedItem is not ComboBoxItem item)
            {
                IncomePlannedAccountRow.Visibility = Visibility.Collapsed;
                return;
            }

            var tag = item.Tag as string ?? "";
            IncomePlannedAccountRow.Visibility =
                string.Equals(tag, "transfer", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ExpensePlannedSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ExpensePlannedEnvelopeRow.Visibility = Visibility.Collapsed;
            ExpensePlannedBankRow.Visibility = Visibility.Collapsed;

            if (ExpensePlannedSourceCombo.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag as string ?? "";
                if (string.Equals(tag, "envelope", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var envNames = DatabaseService.GetEnvelopesNames(_uid) ?? new List<string>();
                        ExpensePlannedEnvelopeCombo.ItemsSource = envNames;
                        if (envNames.Count > 0) ExpensePlannedEnvelopeCombo.SelectedIndex = 0;
                    }
                    catch { }
                    ExpensePlannedEnvelopeRow.Visibility = Visibility.Visible;
                }
                else if (string.Equals(tag, "bank", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var accs = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();
                        ExpensePlannedBankAccountCombo.ItemsSource = accs;
                        ExpensePlannedBankAccountCombo.DisplayMemberPath = "AccountName";
                        ExpensePlannedBankAccountCombo.SelectedValuePath = "Id";
                        if (accs.Count > 0) ExpensePlannedBankAccountCombo.SelectedIndex = 0;
                    }
                    catch { }
                    ExpensePlannedBankRow.Visibility = Visibility.Visible;
                }
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
            try { return DatabaseService.GetOrCreateCategoryId(_uid, catName); }
            catch { return 0; }
        }

        private int? TryResolveEnvelopeIdByName(string? envName)
        {
            if (string.IsNullOrWhiteSpace(envName)) return null;

            try
            {
                var dt = DatabaseService.GetEnvelopesTable(_uid);
                if (dt == null) return null;

                foreach (DataRow r in dt.Rows)
                {
                    var name = (r["Name"]?.ToString() ?? "").Trim();
                    if (string.Equals(name, envName.Trim(), StringComparison.OrdinalIgnoreCase))
                        return Convert.ToInt32(r["Id"]);
                }
            }
            catch { }

            return null;
        }

        // planned panels enablement
        private void UpdateExpensePlannedVisibility()
        {
            ExpensePlannedPanel.Visibility = Visibility.Visible;

            bool hasAmount = TryParseAmount(ExpenseAmountBox.Text, out var amount) && amount > 0m;
            bool hasCategory =
                (ExpenseCategoryBox.SelectedItem is string s && !string.IsNullOrWhiteSpace(s)) ||
                !string.IsNullOrWhiteSpace(ExpenseNewCategoryBox.Text);

            ExpensePlannedButton.IsEnabled = hasAmount && hasCategory;
        }

        private void UpdateIncomePlannedVisibility()
        {
            IncomePlannedPanel.Visibility = Visibility.Visible;

            bool hasAmount = TryParseAmount(IncomeAmountBox.Text, out var amount) && amount > 0m;
            bool hasCategory =
                (IncomeCategoryBox.SelectedItem is string s && !string.IsNullOrWhiteSpace(s)) ||
                !string.IsNullOrWhiteSpace(IncomeNewCategoryBox.Text);

            IncomePlannedButton.IsEnabled = hasAmount && hasCategory;
        }

        private void UpdateTransferPlannedVisibility()
        {
            TransferPlannedPanel.Visibility = Visibility.Visible;

            bool hasAmount = TryParseAmount(TransferAmountBox.Text, out var amount) && amount > 0m;
            bool endpointsSelected =
                TransferFromBox.SelectedItem != null &&
                TransferToBox.SelectedItem != null &&
                TransferFromBox.SelectedIndex != TransferToBox.SelectedIndex;

            TransferPlannedButton.IsEnabled = hasAmount && endpointsSelected;
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

            var sourceTag = sourceItem.Tag as string ?? string.Empty;

            var date = ExpenseDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(ExpenseDescBox.Text) ? null : ExpenseDescBox.Text.Trim();
            var catName = ResolveCategoryName(ExpenseCategoryBox, ExpenseNewCategoryBox);
            int categoryId = GetCategoryIdOrZero(catName);

            // 1) Tworzymy model (bez ustawiania Account na pusto, bo zaraz ustawimy właściwe)
            var eModel = new Expense
            {
                UserId = _uid,
                Amount = (double)amount,
                Date = date,
                Description = desc ?? string.Empty,
                CategoryId = categoryId,
                BudgetId = GetSelectedBudgetId(ExpenseBudgetCombo, date),

                // wartości domyślne (i tak ustawimy je switch-em)
                PaymentKind = Finly.Models.PaymentKind.FreeCash,
                PaymentRefId = null,
                Account = "Wolna gotówka"
            };

            // 2) Ustawiamy źródło płatności (JUŻ PO UTWORZENIU eModel)
            switch (sourceTag)
            {
                case "cash_free":
                    eModel.PaymentKind = Finly.Models.PaymentKind.FreeCash;
                    eModel.PaymentRefId = null;
                    eModel.Account = "Wolna gotówka";
                    break;

                case "cash_saved":
                    eModel.PaymentKind = Finly.Models.PaymentKind.SavedCash;
                    eModel.PaymentRefId = null;
                    eModel.Account = "Odłożona gotówka";
                    break;

                case "envelope":
                    {
                        var envName = ExpenseEnvelopeCombo.SelectedItem as string;
                        var envId = TryResolveEnvelopeIdByName(envName);
                        if (envId == null)
                        {
                            ToastService.Info("Wybierz kopertę.");
                            return;
                        }

                        eModel.PaymentKind = Finly.Models.PaymentKind.Envelope;
                        eModel.PaymentRefId = envId.Value;
                        eModel.Account = $"Koperta: {envName}";
                        break;
                    }

                case "bank":
                    {
                        if (ExpenseBankAccountCombo.SelectedItem is not BankAccountModel acc)
                        {
                            ToastService.Info("Wybierz konto bankowe.");
                            return;
                        }

                        eModel.PaymentKind = Finly.Models.PaymentKind.BankAccount;
                        eModel.PaymentRefId = acc.Id;
                        eModel.Account = $"Konto: {acc.AccountName}";
                        break;
                    }

                default:
                    ToastService.Info("Wybierz poprawne źródło płatności.");
                    return;
            }

            // 3) Zapis + księgowanie tylko raz w DB
            try
            {
                DatabaseService.InsertExpense(eModel);

                ToastService.Success("Dodano wydatek.");

                LoadCategories();
                LoadEnvelopes();
                LoadIncomeAccounts();
                RefreshMoneySummary();

                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać wydatku.\n" + ex.Message);
            }
        }



        private void SaveExpensePlanned_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseAmount(ExpensePlannedAmountBox.Text, out var amount) || amount <= 0m)
            {
                ToastService.Info("Podaj poprawną kwotę.");
                return;
            }

            var date = ExpensePlannedDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(ExpensePlannedDescBox.Text)
                ? null
                : ExpensePlannedDescBox.Text.Trim();

            var catName = ExpensePlannedCategoryBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(catName) && !string.IsNullOrWhiteSpace(ExpensePlannedNewCategoryBox.Text))
                catName = ExpensePlannedNewCategoryBox.Text.Trim();

            int catId;
            try { catId = DatabaseService.GetOrCreateCategoryId(_uid, catName ?? ""); }
            catch { catId = 0; }

            string accountDisplay = string.Empty;
            if (ExpensePlannedSourceCombo.SelectedItem is ComboBoxItem srcItem)
            {
                var tag = srcItem.Tag as string ?? string.Empty;
                switch (tag)
                {
                    case "cash_free": accountDisplay = "Wolna gotówka"; break;
                    case "cash_saved": accountDisplay = "Odłożona gotówka"; break;
                    case "envelope":
                        if (ExpensePlannedEnvelopeCombo.SelectedItem is string envName &&
                            !string.IsNullOrWhiteSpace(envName))
                            accountDisplay = $"Koperta: {envName}";
                        break;
                    case "bank":
                        if (ExpensePlannedBankAccountCombo.SelectedItem is BankAccountModel acc)
                            accountDisplay = $"Konto: {acc.AccountName}";
                        break;
                }
            }

            int? budgetId = GetSelectedBudgetId(ExpensePlannedBudgetCombo, date);

            var eModel = new Expense
            {
                UserId = _uid,
                Amount = (double)amount,
                Date = date,
                Description = desc ?? string.Empty,
                CategoryId = catId,
                IsPlanned = true,
                Account = accountDisplay,
                BudgetId = budgetId
            };

            try
            {
                DatabaseService.InsertExpense(eModel);
                ToastService.Success("Dodano zaplanowany wydatek.");
                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać zaplanowanego wydatku.\n" + ex.Message);
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

            var formTag = formItem.Tag as string ?? string.Empty;
            var date = IncomeDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(IncomeDescBox.Text) ? null : IncomeDescBox.Text.Trim();

            var catName = ResolveCategoryName(IncomeCategoryBox, IncomeNewCategoryBox);
            int? categoryId = GetCategoryIdOrZero(catName);
            if (categoryId == 0) categoryId = null;

            int? incomeBudgetId = null;
            if (IncomeBudgetCombo.SelectedItem is Budget selectedBudget)
                incomeBudgetId = selectedBudget.Id;

            string sourceDisplay;
            BankAccountModel? acc = null;

            switch (formTag)
            {
                case "cash_free":
                    sourceDisplay = "Wolna gotówka";
                    break;

                case "cash_saved":
                    sourceDisplay = "Odłożona gotówka";
                    break;

                case "transfer":
                    if (IncomeAccountCombo.SelectedItem is not BankAccountModel a)
                    {
                        ToastService.Info("Wybierz konto bankowe, na które wpływa przelew.");
                        return;
                    }
                    acc = a;
                    sourceDisplay = $"Konto: {acc.AccountName}";
                    break;

                default:
                    ToastService.Info("Wybierz poprawną formę przychodu.");
                    return;
            }

            try
            {
                // zapis transakcji przychodu
                AddIncomeRaw(_uid, amount, date, categoryId, sourceDisplay, desc, false, incomeBudgetId);

                // aktualizacja sald (tu zostawiamy mechanikę jak było)
                if (formTag == "cash_free")
                {
                    UpdateCashOnHand(_uid, amount);
                }
                else if (formTag == "cash_saved")
                {
                    UpdateSavedCash(_uid, amount);
                    UpdateCashOnHand(_uid, amount);
                }
                else if (formTag == "transfer" && acc != null)
                {
                    IncreaseBankBalance(acc.Id, _uid, amount);
                }

                ToastService.Success("Dodano przychód.");

                LoadCategories();
                LoadIncomeAccounts();
                RefreshMoneySummary();
                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać przychodu.\n" + ex.Message);
            }
        }

        private void SaveIncomePlanned_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseAmount(IncomePlannedAmountBox.Text, out var amount) || amount <= 0m)
            {
                ToastService.Info("Podaj poprawną kwotę.");
                return;
            }

            var date = IncomePlannedDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(IncomePlannedDescBox.Text)
                ? null
                : IncomePlannedDescBox.Text.Trim();

            var sourceTag = (IncomePlannedSourceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

            string sourceDisplay;
            if (sourceTag == "cash_free")
            {
                sourceDisplay = "Wolna gotówka";
            }
            else if (sourceTag == "cash_saved")
            {
                sourceDisplay = "Odłożona gotówka";
            }
            else if (sourceTag == "transfer")
            {
                if (IncomePlannedAccountCombo.SelectedItem is string accName &&
                    !string.IsNullOrWhiteSpace(accName))
                {
                    sourceDisplay = $"Konto: {accName}";
                }
                else
                {
                    ToastService.Info("Wybierz konto docelowe dla planowanego przelewu.");
                    return;
                }
            }
            else
            {
                ToastService.Info("Wybierz formę planowanego przychodu.");
                return;
            }

            var catName = IncomePlannedCategoryBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(catName) &&
                !string.IsNullOrWhiteSpace(IncomePlannedNewCategoryBox.Text))
            {
                catName = IncomePlannedNewCategoryBox.Text.Trim();
            }

            int? catId = null;
            try
            {
                var id = DatabaseService.GetOrCreateCategoryId(_uid, catName ?? string.Empty);
                if (id > 0) catId = id;
            }
            catch { }

            int? plannedIncomeBudgetId = GetSelectedBudgetId(IncomePlannedBudgetCombo, date);

            try
            {
                AddIncomeRaw(_uid, amount, date, catId, sourceDisplay, desc, true, plannedIncomeBudgetId);
                ToastService.Success("Dodano zaplanowany przychód.");
                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać zaplanowanego przychodu.\n" + ex.Message);
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
                    TransactionsFacadeService.TransferBankToBank(_uid, accFrom, accTo, amount);
                    handled = true;
                }

                // BANK -> WOLNA GOTÓWKA
                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.FreeCash &&
                    from.BankAccountId is int accFromBankToFree)
                {
                    TransactionsFacadeService.TransferBankToFreeCash(_uid, accFromBankToFree, amount);
                    handled = true;
                }

                // WOLNA GOTÓWKA -> BANK
                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.BankAccount &&
                    to.BankAccountId is int accToFreeToBank)
                {
                    TransactionsFacadeService.TransferFreeCashToBank(_uid, accToFreeToBank, amount);
                    handled = true;
                }

                // WOLNA GOTÓWKA -> ODŁOŻONA
                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.SavedCash)
                {
                    TransactionsFacadeService.TransferFreeToSaved(_uid, amount);
                    handled = true;
                }

                // ODŁOŻONA -> WOLNA
                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.FreeCash)
                {
                    TransactionsFacadeService.TransferSavedToFree(_uid, amount);
                    handled = true;
                }

                // ODŁOŻONA -> BANK
                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.BankAccount &&
                    to.BankAccountId is int accToSavedToBank)
                {
                    TransactionsFacadeService.TransferSavedToBank(_uid, accToSavedToBank, amount);
                    handled = true;
                }

                // BANK -> ODŁOŻONA
                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.SavedCash &&
                    from.BankAccountId is int accFromBankToSaved)
                {
                    TransactionsFacadeService.TransferBankToSaved(_uid, accFromBankToSaved, amount);
                    handled = true;
                }

                // KOPERTA -> KOPERTA
                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.Envelope &&
                    from.EnvelopeId is int envFrom &&
                    to.EnvelopeId is int envTo)
                {
                    TransactionsFacadeService.TransferEnvelopeToEnvelope(_uid, envFrom, envTo, amount);
                    handled = true;
                }

                // ODŁOŻONA -> KOPERTA
                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.Envelope &&
                    to.EnvelopeId is int envToFromSaved)
                {
                    TransactionsFacadeService.TransferSavedToEnvelope(_uid, envToFromSaved, amount);
                    handled = true;
                }

                // KOPERTA -> ODŁOŻONA
                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.SavedCash &&
                    from.EnvelopeId is int envFromToSaved)
                {
                    TransactionsFacadeService.TransferEnvelopeToSaved(_uid, envFromToSaved, amount);
                    handled = true;
                }

                // WOLNA GOTÓWKA -> KOPERTA
                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.Envelope &&
                    to.EnvelopeId is int envToFromFree)
                {
                    TransactionsFacadeService.TransferFreeToEnvelope(_uid, envToFromFree, amount);
                    handled = true;
                }

                // KOPERTA -> WOLNA GOTÓWKA
                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.FreeCash &&
                    from.EnvelopeId is int envFromToFree)
                {
                    TransactionsFacadeService.TransferEnvelopeToFree(_uid, envFromToFree, amount);
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
                RefreshMoneySummary();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać transferu.\n" + ex.Message);
            }
        }

        private void SaveTransferPlanned_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseAmount(TransferPlannedAmountBox.Text, out var amount) || amount <= 0m)
            {
                ToastService.Info("Podaj poprawną kwotę.");
                return;
            }

            var date = TransferPlannedDatePicker.SelectedDate ?? DateTime.Today;
            var desc = string.IsNullOrWhiteSpace(TransferPlannedDescBox.Text) ? null : TransferPlannedDescBox.Text.Trim();

            var from = TransferPlannedFromBox.SelectedItem as TransferItem;
            var to = TransferPlannedToBox.SelectedItem as TransferItem;
            if (from == null || to == null || from.Key == to.Key)
            {
                ToastService.Info("Wybierz różne konta źródłowe i docelowe.");
                return;
            }

            try
            {
                // Minimalnie: zapisujemy informacyjny planned wpis jako Income (tak jak miałaś),
                // żeby nie rozwalać istniejącej bazy. Logikę planned-transferów dopracujemy później.
                AddIncomeRaw(_uid, amount, date, null, "Transfer (planowany)", desc, true, null);

                ToastService.Success("Dodano zaplanowany transfer.");
                Cancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać zaplanowanego transferu.\n" + ex.Message);
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

        // ====== Income helpers (raw) ======
        private void AddIncomeRaw(int userId, decimal amount, DateTime date, int? categoryId, string source, string? desc, bool isPlanned = false, int? budgetId = null)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Incomes (UserId, Amount, Date, Description, Source, CategoryId, IsPlanned, BudgetId)
VALUES (@u, @a, @d, @desc, @s, @cat, @p, @b);";

            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@d", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@desc", (object?)desc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@s", (object?)source ?? DBNull.Value);

            if (categoryId.HasValue && categoryId.Value > 0)
                cmd.Parameters.AddWithValue("@cat", categoryId.Value);
            else
                cmd.Parameters.AddWithValue("@cat", DBNull.Value);

            cmd.Parameters.AddWithValue("@p", isPlanned ? 1 : 0);
            cmd.Parameters.AddWithValue("@b", (object?)budgetId ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        private void UpdateCashOnHand(int userId, decimal delta)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"UPDATE CashOnHand SET Amount = Amount + @d WHERE UserId=@u;
INSERT INTO CashOnHand(UserId,Amount) SELECT @u,@d WHERE (SELECT changes())=0;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@d", delta);
            cmd.ExecuteNonQuery();
        }

        private void UpdateSavedCash(int userId, decimal delta)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"UPDATE SavedCash SET Amount = Amount + @d WHERE UserId=@u;
INSERT INTO SavedCash(UserId,Amount) SELECT @u,@d WHERE (SELECT changes())=0;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@d", delta);
            cmd.ExecuteNonQuery();
        }

        private void IncreaseBankBalance(int accountId, int userId, decimal delta)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE BankAccounts SET Balance = Balance + @d WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@d", delta);
            cmd.Parameters.AddWithValue("@id", accountId);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }

        private void LoadBudgets()
        {
            try
            {
                var today = DateTime.Today;

                _budgets = BudgetService
                    .GetBudgetsForUser(_uid, from: today, to: today)
                    .ToList();

                void BindBudgetCombo(ComboBox? combo)
                {
                    if (combo == null) return;
                    combo.ItemsSource = _budgets;
                    combo.SelectedValuePath = "Id";
                }

                BindBudgetCombo(ExpenseBudgetCombo);
                BindBudgetCombo(ExpensePlannedBudgetCombo);
                BindBudgetCombo(IncomeBudgetCombo);
                BindBudgetCombo(IncomePlannedBudgetCombo);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się załadować budżetów.\n" + ex.Message);
            }
        }

        private void ExpenseDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadExpenseBudgetsForDate(ExpenseDatePicker.SelectedDate ?? DateTime.Today);
        }

        private void IncomeDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadIncomeBudgetsForDate(IncomeDatePicker.SelectedDate ?? DateTime.Today);
        }

        private void LoadExpenseBudgetsForDate(DateTime date)
        {
            try
            {
                var budgets = BudgetService.GetBudgetsForUser(_uid, from: date, to: date).ToList();

                if (budgets.Any())
                {
                    ExpenseBudgetRow.Visibility = Visibility.Visible;
                    ExpenseBudgetCombo.ItemsSource = budgets;
                    ExpenseBudgetCombo.SelectedIndex = -1;
                }
                else
                {
                    ExpenseBudgetRow.Visibility = Visibility.Collapsed;
                    ExpenseBudgetCombo.ItemsSource = null;
                }
            }
            catch
            {
                ExpenseBudgetRow.Visibility = Visibility.Collapsed;
                ExpenseBudgetCombo.ItemsSource = null;
            }
        }

        private void LoadIncomeBudgetsForDate(DateTime date)
        {
            try
            {
                var budgets = BudgetService.GetBudgetsForUser(_uid, from: date, to: date).ToList();

                if (budgets.Any())
                {
                    IncomeBudgetRow.Visibility = Visibility.Visible;
                    IncomeBudgetCombo.ItemsSource = budgets;
                    IncomeBudgetCombo.SelectedIndex = -1;
                }
                else
                {
                    IncomeBudgetRow.Visibility = Visibility.Collapsed;
                    IncomeBudgetCombo.ItemsSource = null;
                }
            }
            catch
            {
                IncomeBudgetRow.Visibility = Visibility.Collapsed;
                IncomeBudgetCombo.ItemsSource = null;
            }
        }
    }
}
