using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
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

        private bool _initialized;
        private bool _restoring;
        private bool _isUiReady;

        private enum TransferItemKind
        {
            FreeCash,
            SavedCash,
            BankAccount,
            Envelope
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
                if (_restoring) return;

                // KPI można odświeżać zawsze (przy powrocie na stronę też)
                RefreshMoneySummary();

                // ONE-TIME init
                if (_initialized) return;

                _initialized = true;
                _isUiReady = false;

                DatabaseService.EnsureDefaultCategories(_uid);

                // Słowniki / źródła danych
                LoadCategories();
                LoadEnvelopes();
                LoadIncomeAccounts();
                LoadTransferItems();

                // Defaulty tylko jeśli UI nic nie ma ustawione
                var today = DateTime.Today;

                if (ModeTabs.SelectedIndex < 0)
                    ModeTabs.SelectedIndex = 1; // Wydatek

                if (ExpenseSourceCombo.SelectedIndex < 0)
                    ExpenseSourceCombo.SelectedIndex = 0;

                if (IncomeFormTypeCombo.SelectedIndex < 0)
                    IncomeFormTypeCombo.SelectedIndex = 0;

                ExpenseDatePicker.SelectedDate ??= today;
                IncomeDatePicker.SelectedDate ??= today;
                TransferDatePicker.SelectedDate ??= today;

                // Widoczność paneli po ustawieniu SelectedIndex
                ShowPanels((ModeTabs.SelectedItem as TabItem)?.Header?.ToString());

                // Budżety zależne od dat
                LoadExpenseBudgetsForDate(ExpenseDatePicker.SelectedDate ?? today);
                LoadIncomeBudgetsForDate(IncomeDatePicker.SelectedDate ?? today);

                UpdateAllPlannedInfo();

                _isUiReady = true;
            };
        }

        // ================= KPI =================

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
            SetKpiText("InvestmentsText", snap.Investments);
        }

        // ================= ZAKŁADKI =================

        private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || !_isUiReady) return;

            var tab = ModeTabs.SelectedItem as TabItem;
            var header = tab?.Header?.ToString();

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

            UpdateAllPlannedInfo();
        }

        private void ExpenseForm_Changed(object sender, RoutedEventArgs e) { if (_isUiReady) UpdateExpensePlannedInfo(); }
        private void ExpenseForm_Changed(object sender, SelectionChangedEventArgs e) { if (_isUiReady) UpdateExpensePlannedInfo(); }
        private void ExpenseForm_Changed(object sender, TextChangedEventArgs e) { if (_isUiReady) UpdateExpensePlannedInfo(); }

        private void IncomeForm_Changed(object sender, RoutedEventArgs e) { if (_isUiReady) UpdateIncomePlannedInfo(); }
        private void IncomeForm_Changed(object sender, SelectionChangedEventArgs e) { if (_isUiReady) UpdateIncomePlannedInfo(); }
        private void IncomeForm_Changed(object sender, TextChangedEventArgs e) { if (_isUiReady) UpdateIncomePlannedInfo(); }

        private void TransferForm_Changed(object sender, RoutedEventArgs e) { if (_isUiReady) UpdateTransferPlannedInfo(); }
        private void TransferForm_Changed(object sender, SelectionChangedEventArgs e) { if (_isUiReady) UpdateTransferPlannedInfo(); }
        private void TransferForm_Changed(object sender, TextChangedEventArgs e) { if (_isUiReady) UpdateTransferPlannedInfo(); }

        // ================= DANE SŁOWNIKOWE =================

        private void LoadCategories()
        {
            // zachowaj wybory
            var prevExpense = ExpenseCategoryBox.SelectedItem as string;
            var prevIncome = IncomeCategoryBox.SelectedItem as string;
            var prevTransfer = TransferCategoryBox.SelectedItem as string;

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

            if (cats.Count <= 0) return;

            if (!string.IsNullOrWhiteSpace(prevExpense) && cats.Contains(prevExpense))
                ExpenseCategoryBox.SelectedItem = prevExpense;
            else if (ExpenseCategoryBox.SelectedIndex < 0)
                ExpenseCategoryBox.SelectedIndex = 0;

            if (!string.IsNullOrWhiteSpace(prevIncome) && cats.Contains(prevIncome))
                IncomeCategoryBox.SelectedItem = prevIncome;
            else if (IncomeCategoryBox.SelectedIndex < 0)
                IncomeCategoryBox.SelectedIndex = 0;

            if (!string.IsNullOrWhiteSpace(prevTransfer) && cats.Contains(prevTransfer))
                TransferCategoryBox.SelectedItem = prevTransfer;
            else if (TransferCategoryBox.SelectedIndex < 0)
                TransferCategoryBox.SelectedIndex = 0;
        }

        private void LoadEnvelopes()
        {
            var prev = ExpenseEnvelopeCombo.SelectedItem as string;

            try
            {
                var envs = DatabaseService.GetEnvelopesNames(_uid) ?? new List<string>();
                ExpenseEnvelopeCombo.ItemsSource = envs;

                if (envs.Count <= 0)
                {
                    ExpenseEnvelopeCombo.SelectedIndex = -1;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(prev) && envs.Contains(prev))
                    ExpenseEnvelopeCombo.SelectedItem = prev;
                else if (ExpenseEnvelopeCombo.SelectedIndex < 0)
                    ExpenseEnvelopeCombo.SelectedIndex = 0;
            }
            catch
            {
                ExpenseEnvelopeCombo.ItemsSource = Array.Empty<string>();
                ExpenseEnvelopeCombo.SelectedIndex = -1;
            }
        }

        private void LoadIncomeAccounts()
        {
            var prevIncomeAccId = IncomeAccountCombo.SelectedValue as int?;
            var prevExpenseAccId = ExpenseBankAccountCombo.SelectedValue as int?;

            try
            {
                var accs = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();

                IncomeAccountCombo.ItemsSource = accs;
                IncomeAccountCombo.DisplayMemberPath = "AccountName";
                IncomeAccountCombo.SelectedValuePath = "Id";

                ExpenseBankAccountCombo.ItemsSource = accs.ToList();
                ExpenseBankAccountCombo.DisplayMemberPath = "AccountName";
                ExpenseBankAccountCombo.SelectedValuePath = "Id";

                // przywróć wybór jeśli się da
                if (prevIncomeAccId.HasValue)
                    IncomeAccountCombo.SelectedValue = prevIncomeAccId.Value;

                if (prevExpenseAccId.HasValue)
                    ExpenseBankAccountCombo.SelectedValue = prevExpenseAccId.Value;

                // jeśli nadal brak, zostaw -1 (nie wymuszamy defaultu)
            }
            catch
            {
                IncomeAccountCombo.ItemsSource = null;
                ExpenseBankAccountCombo.ItemsSource = null;
            }
        }

        private void LoadTransferItems()
        {
            // zachowaj poprzednie wybory po Key
            var prevFromKey = (TransferFromBox.SelectedItem as TransferItem)?.Key;
            var prevToKey = (TransferToBox.SelectedItem as TransferItem)?.Key;

            var items = new List<TransferItem>
            {
                new TransferItem { Key="free",  Name="Wolna gotówka",    Kind=TransferItemKind.FreeCash },
                new TransferItem { Key="saved", Name="Odłożona gotówka", Kind=TransferItemKind.SavedCash }
            };

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
                        if (r.Table.Columns.Contains("Allocated") && r["Allocated"] != DBNull.Value)
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

            TransferFromBox.ItemsSource = items;
            TransferToBox.ItemsSource = items;

            // przywróć jeśli się da
            if (!string.IsNullOrWhiteSpace(prevFromKey))
                TransferFromBox.SelectedItem = items.FirstOrDefault(x => x.Key == prevFromKey);

            if (!string.IsNullOrWhiteSpace(prevToKey))
                TransferToBox.SelectedItem = items.FirstOrDefault(x => x.Key == prevToKey);

            // fallback defaulty tylko jeśli dalej pusto
            if (TransferFromBox.SelectedIndex < 0 && TransferFromBox.Items.Count > 0)
                TransferFromBox.SelectedIndex = 0;

            if (TransferToBox.SelectedIndex < 0)
            {
                if (TransferToBox.Items.Count > 1) TransferToBox.SelectedIndex = 1;
                else if (TransferToBox.Items.Count > 0) TransferToBox.SelectedIndex = 0;
            }
        }

        private void ExpenseSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || !_isUiReady) return;

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

            UpdateExpensePlannedInfo();
        }

        private void IncomeFormTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || !_isUiReady) return;

            if (IncomeFormTypeCombo.SelectedItem is ComboBoxItem item &&
                string.Equals(item.Tag as string, "transfer", StringComparison.OrdinalIgnoreCase))
            {
                IncomeAccountRow.Visibility = Visibility.Visible;
            }
            else
            {
                IncomeAccountRow.Visibility = Visibility.Collapsed;
            }

            UpdateIncomePlannedInfo();
        }

        // ================= BUDŻETY =================

        private int? GetSelectedBudgetId(ComboBox combo, DateTime date)
        {
            if (combo?.SelectedItem is Budget b)
            {
                if (b.StartDate <= date && date <= b.EndDate)
                    return b.Id;

                ToastService.Info("Wybrany budżet nie obejmuje daty tej operacji – zostanie zapisana bez powiązania z budżetem.");
            }
            return null;
        }

        private void ExpenseDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || !_isUiReady) return;

            var d = ExpenseDatePicker.SelectedDate ?? DateTime.Today;
            LoadExpenseBudgetsForDate(d);
            UpdateExpensePlannedInfo();
        }

        private void IncomeDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || !_isUiReady) return;

            var d = IncomeDatePicker.SelectedDate ?? DateTime.Today;
            LoadIncomeBudgetsForDate(d);
            UpdateIncomePlannedInfo();
        }

        private void TransferDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || !_isUiReady) return;
            UpdateTransferPlannedInfo();
        }

        private void LoadExpenseBudgetsForDate(DateTime date)
        {
            // zachowaj wybór (Id)
            var prevId = (ExpenseBudgetCombo.SelectedItem as Budget)?.Id;

            try
            {
                var budgets = BudgetService.GetBudgetsForUser(_uid, from: date, to: date).ToList();

                ExpenseBudgetRow.Visibility = Visibility.Visible;

                ExpenseBudgetCombo.ItemsSource = budgets;

                bool any = budgets.Any();
                ExpenseBudgetCombo.IsEnabled = any;

                if (any && prevId.HasValue)
                {
                    var match = budgets.FirstOrDefault(b => b.Id == prevId.Value);
                    ExpenseBudgetCombo.SelectedItem = match;
                }
                else
                {
                    ExpenseBudgetCombo.SelectedIndex = -1;
                }

                if (ExpenseBudgetEmptyHint != null)
                    ExpenseBudgetEmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            }
            catch
            {
                ExpenseBudgetRow.Visibility = Visibility.Visible;

                ExpenseBudgetCombo.ItemsSource = null;
                ExpenseBudgetCombo.SelectedIndex = -1;
                ExpenseBudgetCombo.IsEnabled = false;

                if (ExpenseBudgetEmptyHint != null)
                    ExpenseBudgetEmptyHint.Visibility = Visibility.Visible;
            }
        }

        private void LoadIncomeBudgetsForDate(DateTime date)
        {
            var prevId = (IncomeBudgetCombo.SelectedItem as Budget)?.Id;

            try
            {
                var budgets = BudgetService.GetBudgetsForUser(_uid, from: date, to: date).ToList();

                if (budgets.Any())
                {
                    IncomeBudgetRow.Visibility = Visibility.Visible;
                    IncomeBudgetCombo.ItemsSource = budgets;

                    if (prevId.HasValue)
                    {
                        var match = budgets.FirstOrDefault(b => b.Id == prevId.Value);
                        IncomeBudgetCombo.SelectedItem = match;
                    }
                    else
                    {
                        IncomeBudgetCombo.SelectedIndex = -1;
                    }
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

        // ================= PLANOWANE PŁATNOŚCI =================

        private static bool IsPlannedDate(DateTime date) => date.Date > DateTime.Today;

        private void UpdateAllPlannedInfo()
        {
            UpdateExpensePlannedInfo();
            UpdateIncomePlannedInfo();
            UpdateTransferPlannedInfo();
        }

        private void UpdateExpensePlannedInfo()
        {
            if (ExpensePlannedInfoText == null) return;

            var date = ExpenseDatePicker.SelectedDate ?? DateTime.Today;
            if (IsPlannedDate(date))
            {
                ExpensePlannedInfoText.Text = "Ta operacja ma datę w przyszłości — zostanie zapisana jako ZAPLANOWANY wydatek (nie wpłynie od razu na saldo).";
                ExpensePlannedInfoText.Visibility = Visibility.Visible;
            }
            else
            {
                ExpensePlannedInfoText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateIncomePlannedInfo()
        {
            if (IncomePlannedInfoText == null) return;

            var date = IncomeDatePicker.SelectedDate ?? DateTime.Today;
            if (IsPlannedDate(date))
            {
                IncomePlannedInfoText.Text = "Ta operacja ma datę w przyszłości — zostanie zapisana jako ZAPLANOWANY przychód (nie wpłynie od razu na saldo).";
                IncomePlannedInfoText.Visibility = Visibility.Visible;
            }
            else
            {
                IncomePlannedInfoText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateTransferPlannedInfo()
        {
            if (TransferPlannedInfoText == null) return;

            var date = TransferDatePicker.SelectedDate ?? DateTime.Today;
            if (IsPlannedDate(date))
            {
                TransferPlannedInfoText.Text = "Transfer z datą w przyszłości nie jest obsługiwany jako zaplanowany. Ustaw datę na dziś lub wcześniejszą.";
                TransferPlannedInfoText.Visibility = Visibility.Visible;
            }
            else
            {
                TransferPlannedInfoText.Visibility = Visibility.Collapsed;
            }
        }

        // ================= POMOCNICZE =================

        private void ClearAmountErrors()
        {
            ExpenseAmountErrorText.Visibility = Visibility.Collapsed;
            IncomeAmountErrorText.Visibility = Visibility.Collapsed;
            TransferAmountErrorText.Visibility = Visibility.Collapsed;
        }

        private static bool TryParseAmount(string? s, out decimal value)
        {
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("pl-PL"), out value)) return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            return false;
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

        private void ResetForms()
        {
            // podczas resetu blokujemy eventy
            _isUiReady = false;

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

            ExpenseCategoryBox.SelectedIndex = -1;
            IncomeCategoryBox.SelectedIndex = -1;
            TransferCategoryBox.SelectedIndex = -1;

            ExpenseSourceCombo.SelectedIndex = -1;
            ExpenseEnvelopeRow.Visibility = Visibility.Collapsed;
            ExpenseBankRow.Visibility = Visibility.Collapsed;
            ExpenseEnvelopeCombo.SelectedIndex = -1;
            ExpenseBankAccountCombo.SelectedIndex = -1;

            IncomeFormTypeCombo.SelectedIndex = -1;
            IncomeAccountRow.Visibility = Visibility.Collapsed;
            IncomeAccountCombo.SelectedIndex = -1;

            var today = DateTime.Today;
            ExpenseDatePicker.SelectedDate = today;
            IncomeDatePicker.SelectedDate = today;
            TransferDatePicker.SelectedDate = today;

            ExpenseBudgetCombo.SelectedIndex = -1;
            IncomeBudgetCombo.SelectedIndex = -1;

            // odśwież transfer items (i tak resetuje selekcje)
            LoadTransferItems();

            UpdateAllPlannedInfo();

            // przywróć defaulty (jak u Ciebie w Loaded)
            if (ModeTabs.SelectedIndex < 0) ModeTabs.SelectedIndex = 1;
            if (ExpenseSourceCombo.SelectedIndex < 0) ExpenseSourceCombo.SelectedIndex = 0;
            if (IncomeFormTypeCombo.SelectedIndex < 0) IncomeFormTypeCombo.SelectedIndex = 0;

            ShowPanels((ModeTabs.SelectedItem as TabItem)?.Header?.ToString());

            LoadExpenseBudgetsForDate(ExpenseDatePicker.SelectedDate ?? today);
            LoadIncomeBudgetsForDate(IncomeDatePicker.SelectedDate ?? today);

            _isUiReady = true;
        }

        // ================= WYDATEK =================

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
            var isPlanned = IsPlannedDate(date);

            var desc = string.IsNullOrWhiteSpace(ExpenseDescBox.Text) ? null : ExpenseDescBox.Text.Trim();
            var catName = ResolveCategoryName(ExpenseCategoryBox, ExpenseNewCategoryBox);
            int categoryId = GetCategoryIdOrZero(catName);

            var eModel = new Expense
            {
                UserId = _uid,
                Amount = (double)amount,
                Date = date,
                Description = desc ?? string.Empty,
                CategoryId = categoryId,
                BudgetId = GetSelectedBudgetId(ExpenseBudgetCombo, date),
                IsPlanned = isPlanned,

                PaymentKind = PaymentKind.FreeCash,
                PaymentRefId = null,
                Account = "Wolna gotówka"
            };

            switch (sourceTag)
            {
                case "cash_free":
                    eModel.PaymentKind = PaymentKind.FreeCash;
                    eModel.PaymentRefId = null;
                    eModel.Account = "Wolna gotówka";
                    break;

                case "cash_saved":
                    eModel.PaymentKind = PaymentKind.SavedCash;
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

                        eModel.PaymentKind = PaymentKind.Envelope;
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

                        eModel.PaymentKind = PaymentKind.BankAccount;
                        eModel.PaymentRefId = acc.Id;
                        eModel.Account = $"Konto: {acc.AccountName}";
                        break;
                    }

                default:
                    ToastService.Info("Wybierz poprawne źródło płatności.");
                    return;
            }

            try
            {
                TransactionsFacadeService.SpendExpense(eModel);

                ToastService.Success(isPlanned ? "Dodano zaplanowany wydatek." : "Dodano wydatek.");

                // Odśwież słowniki po dodaniu (mogła dojść nowa kategoria)
                LoadCategories();
                LoadEnvelopes();
                LoadIncomeAccounts();

                RefreshMoneySummary();

                ResetForms();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać wydatku.\n" + ex.Message);
            }
        }

        // ================= PRZYCHÓD =================

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

            var formTag = (formItem.Tag as string ?? string.Empty).Trim();

            var date = IncomeDatePicker.SelectedDate ?? DateTime.Today;
            var isPlanned = IsPlannedDate(date);

            var desc = string.IsNullOrWhiteSpace(IncomeDescBox.Text)
                ? null
                : IncomeDescBox.Text.Trim();

            var catName = ResolveCategoryName(IncomeCategoryBox, IncomeNewCategoryBox);
            int? categoryId = GetCategoryIdOrZero(catName);
            if (categoryId == 0) categoryId = null;

            int? incomeBudgetId = GetSelectedBudgetId(IncomeBudgetCombo, date);

            PaymentKind paymentKind;
            int? paymentRefId;
            string sourceDisplay;

            switch (formTag)
            {
                case "cash_free":
                    paymentKind = PaymentKind.FreeCash;
                    paymentRefId = null;
                    sourceDisplay = "Wolna gotówka";
                    break;

                case "cash_saved":
                    paymentKind = PaymentKind.SavedCash;
                    paymentRefId = null;
                    sourceDisplay = "Odłożona gotówka";
                    break;

                case "transfer":
                    if (IncomeAccountCombo.SelectedItem is not BankAccountModel acc)
                    {
                        ToastService.Info("Wybierz konto bankowe, na które wpływa przelew.");
                        return;
                    }

                    paymentKind = PaymentKind.BankAccount;
                    paymentRefId = acc.Id;
                    sourceDisplay = $"Konto: {acc.AccountName}";
                    break;

                default:
                    ToastService.Info("Wybierz poprawną formę przychodu.");
                    return;
            }

            try
            {
                DatabaseService.InsertIncome(
                    userId: _uid,
                    amount: amount,
                    date: date,
                    categoryId: categoryId,
                    source: sourceDisplay,
                    description: desc,
                    isPlanned: isPlanned,
                    budgetId: incomeBudgetId,
                    paymentKind: paymentKind,
                    paymentRefId: paymentRefId
                );

                ToastService.Success(isPlanned ? "Dodano zaplanowany przychód." : "Dodano przychód.");

                LoadCategories();
                LoadIncomeAccounts();
                RefreshMoneySummary();

                ResetForms();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się dodać przychodu.\n" + ex.Message);
            }
        }

        // ================= TRANSFER =================

        private void SaveTransfer_Click(object sender, RoutedEventArgs e)
        {
            ClearAmountErrors();

            if (!TryParseAmount(TransferAmountBox.Text, out var amount) || amount <= 0m)
            {
                TransferAmountErrorText.Visibility = Visibility.Visible;
                return;
            }

            var date = TransferDatePicker.SelectedDate ?? DateTime.Today;
            if (IsPlannedDate(date))
            {
                ToastService.Info("Transferów zaplanowanych nie obsługujemy. Ustaw datę na dziś lub wcześniejszą.");
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

                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.BankAccount &&
                    from.BankAccountId is int accFrom &&
                    to.BankAccountId is int accTo)
                {
                    TransactionsFacadeService.TransferBankToBank(_uid, accFrom, accTo, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.FreeCash &&
                    from.BankAccountId is int accFromBankToFree)
                {
                    TransactionsFacadeService.TransferBankToFreeCash(_uid, accFromBankToFree, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.BankAccount &&
                    to.BankAccountId is int accToFreeToBank)
                {
                    TransactionsFacadeService.TransferFreeCashToBank(_uid, accToFreeToBank, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.SavedCash)
                {
                    TransactionsFacadeService.TransferFreeToSaved(_uid, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.FreeCash)
                {
                    TransactionsFacadeService.TransferSavedToFree(_uid, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.BankAccount &&
                    to.BankAccountId is int accToSavedToBank)
                {
                    TransactionsFacadeService.TransferSavedToBank(_uid, accToSavedToBank, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.BankAccount &&
                    to.Kind == TransferItemKind.SavedCash &&
                    from.BankAccountId is int accFromBankToSaved)
                {
                    TransactionsFacadeService.TransferBankToSaved(_uid, accFromBankToSaved, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.Envelope &&
                    from.EnvelopeId is int envFrom &&
                    to.EnvelopeId is int envTo)
                {
                    TransactionsFacadeService.TransferEnvelopeToEnvelope(_uid, envFrom, envTo, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.SavedCash &&
                    to.Kind == TransferItemKind.Envelope &&
                    to.EnvelopeId is int envToFromSaved)
                {
                    TransactionsFacadeService.TransferSavedToEnvelope(_uid, envToFromSaved, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.Envelope &&
                    to.Kind == TransferItemKind.SavedCash &&
                    from.EnvelopeId is int envFromToSaved)
                {
                    TransactionsFacadeService.TransferEnvelopeToSaved(_uid, envFromToSaved, amount);
                    handled = true;
                }

                if (!handled &&
                    from.Kind == TransferItemKind.FreeCash &&
                    to.Kind == TransferItemKind.Envelope &&
                    to.EnvelopeId is int envToFromFree)
                {
                    TransactionsFacadeService.TransferFreeToEnvelope(_uid, envToFromFree, amount);
                    handled = true;
                }

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

                RefreshMoneySummary();
                ResetForms();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać transferu.\n" + ex.Message);
            }
        }

        // ================= POMOCNICZE =================

        private void CreateBudgetFromExpense_Click(object sender, RoutedEventArgs e)
        {
            var date = ExpenseDatePicker.SelectedDate ?? DateTime.Today;

            var dlg = new Finly.Views.Dialogs.EditBudgetDialog
            {
                Owner = Window.GetWindow(this)
            };

            dlg.Budget.Name = "";
            dlg.Budget.StartDate = date;
            dlg.Budget.Type = "Monthly";
            dlg.Budget.PlannedAmount = 0m;

            var ok = dlg.ShowDialog();
            if (ok != true) return;

            var newId = BudgetService.InsertBudget(_uid, dlg.Budget);

            LoadExpenseBudgetsForDate(date);

            if (ExpenseBudgetCombo.ItemsSource is System.Collections.IEnumerable src)
            {
                foreach (var item in src)
                {
                    if (item is Finly.Models.Budget b && b.Id == newId)
                    {
                        ExpenseBudgetCombo.SelectedItem = b;
                        break;
                    }
                }
            }

            ToastService.Success("Dodano budżet.");
        }

        private void CreateBudgetFromIncome_Click(object sender, RoutedEventArgs e)
        {
            var date = IncomeDatePicker.SelectedDate ?? DateTime.Today;

            var dlg = new Finly.Views.Dialogs.EditBudgetDialog
            {
                Owner = Window.GetWindow(this)
            };

            dlg.Budget.Name = "";
            dlg.Budget.StartDate = date;
            dlg.Budget.Type = "Monthly";
            dlg.Budget.PlannedAmount = 0m;

            var ok = dlg.ShowDialog();
            if (ok != true) return;

            var newId = BudgetService.InsertBudget(_uid, dlg.Budget);

            LoadIncomeBudgetsForDate(date);

            if (IncomeBudgetCombo.ItemsSource is System.Collections.IEnumerable src)
            {
                foreach (var item in src)
                {
                    if (item is Finly.Models.Budget b && b.Id == newId)
                    {
                        IncomeBudgetCombo.SelectedItem = b;
                        break;
                    }
                }
            }

            ToastService.Success("Dodano budżet.");
        }

    }
}
