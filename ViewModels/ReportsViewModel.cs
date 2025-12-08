using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Finly.Helpers;
using Finly.Models;
using Finly.Services;
using System.Collections.Generic;
using System.Data;

namespace Finly.ViewModels
{
    public class ReportsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ReportsViewModel()
        {
            _fromDate = DateTime.Today;
            _toDate = DateTime.Today;

            // initialize collections to be filled dynamically
            Accounts   = new ObservableCollection<string>();
            Categories = new ObservableCollection<string>();
            Envelopes  = new ObservableCollection<string>();
            Tags = new ObservableCollection<string> { "Brak", "Podró¿e", "Praca" };
            Currencies = new ObservableCollection<string> { "PLN", "EUR", "USD" };
            Templates = new ObservableCollection<string> { "Domyœlny", "Miesiêczny przegl¹d" };

            Insights = new ObservableCollection<string>();
            KPIList = new ObservableCollection<KeyValuePair<string, string>>();

            SelectedSource = SourceType.All;

            RefreshCommand = new RelayCommand(_ => Refresh());
            SavePresetCommand = new RelayCommand(_ => SavePreset());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());
            ExportExcelCommand = new RelayCommand(_ => ExportExcel());
            ExportPdfCommand = new RelayCommand(_ => ExportPdf());
            BackCommand = new RelayCommand(_ => BackToSummary());

            BankAccounts = new ObservableCollection<BankAccountModel>();
            Details = new ObservableCollection<CategoryAmount>();
            FilteredTransactions = new ObservableCollection<TransactionDto>();
            _transactionsSnapshot = new List<TransactionDto>();

            LoadAccountsAndEnvelopes();
        }

        private void LoadAccountsAndEnvelopes()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();

                // ===== Konta bankowe =====
                BankAccounts = new ObservableCollection<BankAccountModel>();
                BankAccounts.Add(new BankAccountModel { Id = 0, UserId = uid, AccountName = "Wszystkie konta bankowe", BankName = "" });
                foreach (var a in DatabaseService.GetAccounts(uid))
                    BankAccounts.Add(a);

                // ===== Koperty =====
                try
                {
                    var envs = DatabaseService.GetEnvelopesNames(uid) ?? new List<string>();
                    Envelopes.Clear();
                    Envelopes.Add("Wszystkie koperty");
                    foreach (var e in envs)
                        Envelopes.Add(e);
                }
                catch { }

                // ===== Kategorie =====
                try
                {
                    var cats = DatabaseService.GetCategoriesByUser(uid) ?? new List<string>();
                    Categories.Clear();
                    Categories.Add("Wszystkie kategorie");
                    foreach (var c in cats)
                        Categories.Add(c);
                }
                catch { }
            }
            catch { }
        }

        public enum SourceType { All = 0, FreeCash = 1, SavedCash = 2, BankAccounts = 3, Envelopes = 4 }
        public Array SourceOptions => Enum.GetValues(typeof(SourceType));

        // Rodzaj wybranego okresu czasowego
        private enum PeriodKind
        {
            Custom,
            Today,
            ThisWeek,
            ThisMonth,
            ThisQuarter,
            ThisYear
        }

        private string _currentPeriodName = "ten okres";
        public string CurrentPeriodName
        {
            get => _currentPeriodName;
            private set { _currentPeriodName = value; Raise(nameof(CurrentPeriodName)); }
        }

        private string _previousPeriodName = "poprzedni okres";
        public string PreviousPeriodName
        {
            get => _previousPeriodName;
            private set { _previousPeriodName = value; Raise(nameof(PreviousPeriodName)); }
        }

        // Teksty do kart po prawej (u¿ywane te¿ w PDF)
        private string _analyzedPeriodLabel = string.Empty;
        public string AnalyzedPeriodLabel
        {
            get => _analyzedPeriodLabel;
            private set { _analyzedPeriodLabel = value; Raise(nameof(AnalyzedPeriodLabel)); }
        }

        private string _comparisonPeriodLabel = string.Empty;
        public string ComparisonPeriodLabel
        {
            get => _comparisonPeriodLabel;
            private set { _comparisonPeriodLabel = value; Raise(nameof(ComparisonPeriodLabel)); }
        }

        private static PeriodKind DetectPeriodKind(DateTime from, DateTime to, DateTime today)
        {
            from = from.Date;
            to = to.Date;
            today = today.Date;

            if (from == today && to == today)
                return PeriodKind.Today;

            var startOfWeek = StartOfWeek(today, DayOfWeek.Monday);
            var endOfWeek = startOfWeek.AddDays(6);
            if (from == startOfWeek && to == endOfWeek)
                return PeriodKind.ThisWeek;

            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
            if (from == startOfMonth && to == endOfMonth)
                return PeriodKind.ThisMonth;

            int quarterIndex = (today.Month - 1) / 3;
            var startOfQuarter = new DateTime(today.Year, quarterIndex * 3 + 1, 1);
            var endOfQuarter = startOfQuarter.AddMonths(3).AddDays(-1);
            if (from == startOfQuarter && to == endOfQuarter)
                return PeriodKind.ThisQuarter;

            var startOfYear = new DateTime(today.Year, 1, 1);
            var endOfYear = new DateTime(today.Year, 12, 31);
            if (from == startOfYear && to == endOfYear)
                return PeriodKind.ThisYear;

            return PeriodKind.Custom;
        }

        private static DateTime StartOfWeek(DateTime dt, DayOfWeek startOfWeek)
        {
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }

        private static (string current, string previous) GetPeriodNames(PeriodKind kind)
        {
            return kind switch
            {
                PeriodKind.Today => ("dzisiaj", "wczoraj"),
                PeriodKind.ThisWeek => ("ten tydzieñ", "poprzedni tydzieñ"),
                PeriodKind.ThisMonth => ("ten miesi¹c", "poprzedni miesi¹c"),
                PeriodKind.ThisQuarter => ("ten kwarta³", "poprzedni kwarta³"),
                PeriodKind.ThisYear => ("ten rok", "poprzedni rok"),
                _ => ("ten okres", "poprzedni okres")
            };
        }

        // ========= zakres dat (powi¹zany z PeriodBarControl) =========
        private DateTime _fromDate;
        private DateTime _toDate;
        public DateTime FromDate
        {
            get => _fromDate;
            set
            {
                if (_fromDate != value)
                {
                    _fromDate = value;
                    Raise(nameof(FromDate));
                }
            }
        }
        public DateTime ToDate
        {
            get => _toDate;
            set
            {
                if (_toDate != value)
                {
                    _toDate = value;
                    Raise(nameof(ToDate));
                }
            }
        }

        // ========= listy do filtrów =========
        public ObservableCollection<string> Accounts { get; }
        public ObservableCollection<string> Categories { get; }
        public ObservableCollection<string> Envelopes { get; }
        public ObservableCollection<string> Tags { get; }
        public ObservableCollection<string> Currencies { get; }
        public ObservableCollection<string> Templates { get; }

        private string _selectedAccount = "Wszystkie konta";
        public string SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (_selectedAccount != value)
                {
                    _selectedAccount = value;
                    Raise(nameof(SelectedAccount));
                }
            }
        }

        private string _selectedCategory = "Wszystkie kategorie";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    _selectedCategory = value;
                    Raise(nameof(SelectedCategory));
                }
            }
        }

        private string _selectedTemplate = "Domyœlny";
        public string SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (_selectedTemplate != value)
                {
                    _selectedTemplate = value;
                    Raise(nameof(SelectedTemplate));
                }
            }
        }

        private SourceType _selectedSource;
        public SourceType SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (_selectedSource != value)
                {
                    _selectedSource = value;
                    Raise(nameof(SelectedSource));
                    Raise(nameof(ShowBankSelector));
                    Raise(nameof(ShowEnvelopeSelector));
                }
            }
        }
        public bool ShowBankSelector => SelectedSource == SourceType.BankAccounts;
        public bool ShowEnvelopeSelector => SelectedSource == SourceType.Envelopes;

        private ObservableCollection<BankAccountModel> _bankAccounts = new();
        public ObservableCollection<BankAccountModel> BankAccounts
        {
            get => _bankAccounts;
            set
            {
                _bankAccounts = value;
                Raise(nameof(BankAccounts));
            }
        }
        private BankAccountModel? _selectedBankAccount;
        public BankAccountModel? SelectedBankAccount
        {
            get => _selectedBankAccount;
            set
            {
                _selectedBankAccount = value;
                Raise(nameof(SelectedBankAccount));
            }
        }

        private string _selectedEnvelope = "Wszystkie koperty";
        public string SelectedEnvelope
        {
            get => _selectedEnvelope;
            set
            {
                _selectedEnvelope = value;
                Raise(nameof(SelectedEnvelope));
            }
        }

        public ObservableCollection<string> Insights { get; }
        public ObservableCollection<KeyValuePair<string, string>> KPIList { get; }

        // ========= komendy =========
        public ICommand RefreshCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand BackCommand { get; }

        public void ResetFilters()
        {
            SelectedAccount = Accounts.Count > 0 ? Accounts[0] : string.Empty;
            SelectedCategory = Categories.Count > 0 ? Categories[0] : string.Empty;
            SelectedTemplate = Templates.Count > 0 ? Templates[0] : string.Empty;
            SelectedSource = SourceType.All;
            SelectedBankAccount = BankAccounts.FirstOrDefault();
            SelectedEnvelope = Envelopes.Count > 0 ? Envelopes[0] : "Wszystkie koperty";
        }

        // ========= modele danych do widoku =========

        public class CategoryAmount
        {
            public string Name { get; set; } = "";
            public decimal Amount { get; set; }
            public double SharePercent { get; set; }
        }

        public class TransactionDto
        {
            public int Id { get; set; }
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Description { get; set; } = "";
            public string Category { get; set; } = "";
        }

        public ObservableCollection<CategoryAmount> Details { get; private set; } = new();
        public ObservableCollection<TransactionDto> FilteredTransactions { get; private set; } = new();
        private List<TransactionDto> _transactionsSnapshot;

        private Dictionary<string, decimal> _chartTotals = new();
        public Dictionary<string, decimal> ChartTotals
        {
            get => _chartTotals;
            private set
            {
                _chartTotals = value;
                Raise(nameof(ChartTotals));
            }
        }

        private decimal _chartTotalAll = 0m;
        public decimal ChartTotalAll
        {
            get => _chartTotalAll;
            private set
            {
                _chartTotalAll = value;
                Raise(nameof(ChartTotalAll));
            }
        }

        private bool _isDrilldown = false;
        public bool IsDrilldownActive
        {
            get => _isDrilldown;
            private set
            {
                _isDrilldown = value;
                Raise(nameof(IsDrilldownActive));
                Raise(nameof(IsSummaryActive));
            }
        }
        public bool IsSummaryActive => !IsDrilldownActive;

        // ========= prawa kolumna: sumy bie¿¹cego okresu =========
        private decimal _expensesTotal = 0m;
        public decimal ExpensesTotal
        {
            get => _expensesTotal;
            private set
            {
                if (_expensesTotal != value)
                {
                    _expensesTotal = value;
                    Raise(nameof(ExpensesTotal));
                    Raise(nameof(ExpensesTotalStr));
                }
            }
        }
        public string ExpensesTotalStr => ExpensesTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

        private decimal _incomesTotal = 0m;
        public decimal IncomesTotal
        {
            get => _incomesTotal;
            private set
            {
                if (_incomesTotal != value)
                {
                    _incomesTotal = value;
                    Raise(nameof(IncomesTotal));
                    Raise(nameof(IncomesTotalStr));
                }
            }
        }
        public string IncomesTotalStr => IncomesTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

        private decimal _balanceTotal = 0m;
        public decimal BalanceTotal
        {
            get => _balanceTotal;
            private set
            {
                if (_balanceTotal != value)
                {
                    _balanceTotal = value;
                    Raise(nameof(BalanceTotal));
                    Raise(nameof(BalanceTotalStr));
                }
            }
        }
        public string BalanceTotalStr => BalanceTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

        // ========= poprzedni okres + ró¿nice =========
        private DateTime _previousFromDate;
        private DateTime _previousToDate;

        private decimal _previousExpensesTotal;
        public decimal PreviousExpensesTotal
        {
            get => _previousExpensesTotal;
            private set
            {
                _previousExpensesTotal = value;
                Raise(nameof(PreviousExpensesTotal));
                Raise(nameof(PreviousExpensesTotalStr));
            }
        }
        public string PreviousExpensesTotalStr => PreviousExpensesTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

        private decimal _previousIncomesTotal;
        public decimal PreviousIncomesTotal
        {
            get => _previousIncomesTotal;
            private set
            {
                _previousIncomesTotal = value;
                Raise(nameof(PreviousIncomesTotal));
                Raise(nameof(PreviousIncomesTotalStr));
            }
        }
        public string PreviousIncomesTotalStr => PreviousIncomesTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

        private decimal _previousBalanceTotal;
        public decimal PreviousBalanceTotal
        {
            get => _previousBalanceTotal;
            private set
            {
                _previousBalanceTotal = value;
                Raise(nameof(PreviousBalanceTotal));
                Raise(nameof(PreviousBalanceTotalStr));
            }
        }
        public string PreviousBalanceTotalStr => PreviousBalanceTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

        private string _expensesChangePercentStr = "0%";
        public string ExpensesChangePercentStr
        {
            get => _expensesChangePercentStr;
            private set
            {
                _expensesChangePercentStr = value;
                Raise(nameof(ExpensesChangePercentStr));
            }
        }

        private string _incomesChangePercentStr = "0%";
        public string IncomesChangePercentStr
        {
            get => _incomesChangePercentStr;
            private set
            {
                _incomesChangePercentStr = value;
                Raise(nameof(IncomesChangePercentStr));
            }
        }

        private string _balanceChangePercentStr = "0%";
        public string BalanceChangePercentStr
        {
            get => _balanceChangePercentStr;
            private set
            {
                _balanceChangePercentStr = value;
                Raise(nameof(BalanceChangePercentStr));
            }
        }

        // ========= pomocnicze metody agreguj¹ce =========

        private void PopulateFromDataTable(DataTable dt)
        {
            _transactionsSnapshot.Clear();
            FilteredTransactions.Clear();

            foreach (DataRow r in dt.Rows)
            {
                var t = new TransactionDto
                {
                    Id = dt.Columns.Contains("Id") && r["Id"] != DBNull.Value ? Convert.ToInt32(r["Id"]) : 0,
                    Date = dt.Columns.Contains("Date") && r["Date"] != DBNull.Value ? DateTime.Parse(r["Date"].ToString()!) : DateTime.MinValue,
                    Amount = dt.Columns.Contains("Amount") && r["Amount"] != DBNull.Value ? Convert.ToDecimal(r["Amount"]) : 0m,
                    Description = dt.Columns.Contains("Description") && r["Description"] != DBNull.Value ? r["Description"].ToString()! : "",
                    Category = dt.Columns.Contains("CategoryName") && r["CategoryName"] != DBNull.Value ? r["CategoryName"].ToString()! : "(brak)"
                };

                _transactionsSnapshot.Add(t);
                FilteredTransactions.Add(t);
            }

            _detailsClearAndGroup(dt);
        }

        private void _detailsClearAndGroup(DataTable dt)
        {
            Details.Clear();
            var groups = dt.AsEnumerable()
                .GroupBy(r => r.Field<string>("CategoryName") ?? "(brak)")
                .Select(g => new { Name = g.Key, Total = g.Sum(r => Convert.ToDecimal(r.Field<object>("Amount"))) })
                .OrderByDescending(x => x.Total)
                .ToList();

            var total = groups.Sum(x => x.Total);
            ChartTotals = groups.ToDictionary(x => x.Name, x => x.Total);
            ChartTotalAll = total;

            foreach (var g in groups)
            {
                Details.Add(new CategoryAmount
                {
                    Name = g.Name,
                    Amount = g.Total,
                    SharePercent = total > 0 ? (double)(g.Total / total * 100m) : 0.0
                });
            }
        }

        public void ShowDrilldown(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return;
            IsDrilldownActive = true;
            var list = _transactionsSnapshot
                .Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.Date)
                .ToList();

            FilteredTransactions.Clear();
            foreach (var t in list)
                FilteredTransactions.Add(t);
        }

        public void BackToSummary()
        {
            IsDrilldownActive = false;
            FilteredTransactions.Clear();
            foreach (var t in _transactionsSnapshot.OrderByDescending(t => t.Date))
                FilteredTransactions.Add(t);
        }

        private (DateTime PrevFrom, DateTime PrevTo) GetPreviousPeriod(DateTime currentFrom, DateTime currentTo)
        {
            var from = currentFrom.Date;
            var to = currentTo.Date;
            if (to < from)
            {
                var tmp = from;
                from = to;
                to = tmp;
            }

            int length = (to - from).Days + 1;
            var prevTo = from.AddDays(-1);
            var prevFrom = prevTo.AddDays(-length + 1);
            return (prevFrom, prevTo);
        }

        private DataTable GetFilteredExpensesDataTable(int uid, DateTime from, DateTime to, int? accountId)
        {
            DataTable dt = DatabaseService.GetExpenses(uid, from, to, null, null, accountId);

            IEnumerable<DataRow> rows = dt.AsEnumerable();
            if (SelectedSource == SourceType.FreeCash)
            {
                rows = rows.Where(r => r.IsNull("AccountId"));
            }
            else if (SelectedSource == SourceType.SavedCash)
            {
                rows = rows.Where(r => r.IsNull("AccountId"));
            }
            else if (SelectedSource == SourceType.Envelopes)
            {
                if (!string.IsNullOrWhiteSpace(SelectedEnvelope) && SelectedEnvelope != "Wszystkie koperty")
                    rows = rows.Where(r => (r.Field<string>("Description") ?? "").IndexOf(SelectedEnvelope, StringComparison.OrdinalIgnoreCase) >= 0);
                else
                    rows = rows.Where(r => r.IsNull("AccountId"));
            }

            return rows.CopyToDataTableOrEmpty();
        }

        private decimal SumAmount(DataTable dt, string columnName)
        {
            decimal total = 0m;
            if (dt.Rows.Count > 0 && dt.Columns.Contains(columnName))
            {
                foreach (DataRow r in dt.Rows)
                {
                    if (r[columnName] != DBNull.Value)
                        total += Convert.ToDecimal(r[columnName]);
                }
            }
            return total;
        }

        private string FormatPercentChange(decimal previous, decimal current)
        {
            if (previous == 0m)
            {
                if (current == 0m) return "0% (bez zmian)";
                return "n/d (brak danych)";
            }

            var diffPct = (current - previous) / previous * 100m;
            var sign = diffPct > 0 ? "+" : "";
            return sign + diffPct.ToString("N1", CultureInfo.CurrentCulture) + " %";
        }

        private void Refresh()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                if (uid <= 0)
                {
                    MessageBox.Show("Brak zalogowanego u¿ytkownika.", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int? accountId = null;
                if (SelectedSource == SourceType.BankAccounts && SelectedBankAccount != null && SelectedBankAccount.Id > 0)
                    accountId = SelectedBankAccount.Id;

                // ====== aktualny okres ======
                var currentDt = GetFilteredExpensesDataTable(uid, FromDate, ToDate, accountId);

                FilteredTransactions.Clear();
                Details.Clear();
                if (currentDt.Rows.Count > 0)
                    PopulateFromDataTable(currentDt);
                else
                {
                    ChartTotals = new Dictionary<string, decimal>();
                    ChartTotalAll = 0m;
                }

                // wydatki bie¿¹ce
                ExpensesTotal = SumAmount(currentDt, "Amount");

                // przychody bie¿¹ce
                decimal incomes = 0m;
                try
                {
                    var inc = DatabaseService.GetIncomeBySourceSafe(uid, FromDate, ToDate) ?? new List<DatabaseService.CategoryAmountDto>();
                    incomes = inc.Sum(x => x.Amount);
                }
                catch
                {
                    incomes = 0m;
                }
                IncomesTotal = incomes;

                BalanceTotal = IncomesTotal - ExpensesTotal;

                // ====== poprzedni okres o tej samej d³ugoœci ======
                var prev = GetPreviousPeriod(FromDate, ToDate);
                _previousFromDate = prev.PrevFrom;
                _previousToDate = prev.PrevTo;

                // okresy nazwane
                var kind = DetectPeriodKind(FromDate.Date, ToDate.Date, DateTime.Today);
                var (currentName, previousName) = GetPeriodNames(kind);
                CurrentPeriodName = currentName;
                PreviousPeriodName = previousName;

                AnalyzedPeriodLabel = $"{CurrentPeriodName} ({FromDate:dd.MM.yyyy} – {ToDate:dd.MM.yyyy})";
                ComparisonPeriodLabel = $"{PreviousPeriodName} ({prev.PrevFrom:dd.MM.yyyy} – {prev.PrevTo:dd.MM.yyyy})";

                var prevDt = GetFilteredExpensesDataTable(uid, prev.PrevFrom, prev.PrevTo, accountId);
                PreviousExpensesTotal = SumAmount(prevDt, "Amount");

                decimal prevIncomes = 0m;
                try
                {
                    var pinc = DatabaseService.GetIncomeBySourceSafe(uid, prev.PrevFrom, prev.PrevTo) ?? new List<DatabaseService.CategoryAmountDto>();
                    prevIncomes = pinc.Sum(x => x.Amount);
                }
                catch
                {
                    prevIncomes = 0m;
                }
                PreviousIncomesTotal = prevIncomes;
                PreviousBalanceTotal = PreviousIncomesTotal - PreviousExpensesTotal;

                // ====== ró¿nice procentowe ======
                ExpensesChangePercentStr = FormatPercentChange(PreviousExpensesTotal, ExpensesTotal);
                IncomesChangePercentStr = FormatPercentChange(PreviousIncomesTotal, IncomesTotal);
                BalanceChangePercentStr = FormatPercentChange(PreviousBalanceTotal, BalanceTotal);

                IsDrilldownActive = false;

                // ====== KPI & insighty ======
                KPIList.Clear();
                KPIList.Add(new KeyValuePair<string, string>($"Suma wydatków ({CurrentPeriodName})", ExpensesTotalStr));
                KPIList.Add(new KeyValuePair<string, string>($"Suma wydatków ({PreviousPeriodName})", PreviousExpensesTotalStr));
                KPIList.Add(new KeyValuePair<string, string>("Zmiana wydatków", ExpensesChangePercentStr));

                KPIList.Add(new KeyValuePair<string, string>($"Suma przychodów ({CurrentPeriodName})", IncomesTotalStr));
                KPIList.Add(new KeyValuePair<string, string>($"Suma przychodów ({PreviousPeriodName})", PreviousIncomesTotalStr));
                KPIList.Add(new KeyValuePair<string, string>("Zmiana przychodów", IncomesChangePercentStr));

                KPIList.Add(new KeyValuePair<string, string>($"Saldo ({CurrentPeriodName})", BalanceTotalStr));
                KPIList.Add(new KeyValuePair<string, string>($"Saldo ({PreviousPeriodName})", PreviousBalanceTotalStr));
                KPIList.Add(new KeyValuePair<string, string>("Zmiana salda", BalanceChangePercentStr));

                Insights.Clear();
                Insights.Add($"Analizowany okres: {AnalyzedPeriodLabel}");
                Insights.Add($"Porównanie z okresem: {ComparisonPeriodLabel}");
                Insights.Add($"Wydajesz {ExpensesChangePercentStr} {(ExpensesTotal > PreviousExpensesTotal ? "wiêcej" : ExpensesTotal < PreviousExpensesTotal ? "mniej" : "(bez zmian)")} ni¿ w {PreviousPeriodName}.");
                Insights.Add($"Twoje przychody s¹ {IncomesChangePercentStr} {(IncomesTotal > PreviousIncomesTotal ? "wy¿sze" : IncomesTotal < PreviousIncomesTotal ? "ni¿sze" : "(bez zmian)")} ni¿ w {PreviousPeriodName}.");

                Raise(nameof(Details));
                Raise(nameof(ChartTotals));
                Raise(nameof(ChartTotalAll));
                Raise(nameof(FilteredTransactions));
            }
            catch (Exception ex)
            {
                MessageBox.Show("B³¹d odczytu raportu: " + ex.Message, "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePreset()
        {
            MessageBox.Show($"Zapisano preset: {SelectedTemplate}", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportCsv()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "report_export.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Category,Amount,SharePercent");
                foreach (var d in Details)
                    sb.AppendLine($"{d.Name},{d.Amount:N2},{d.SharePercent:N1}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Eksport CSV zapisano na pulpicie: {path}", "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"B³¹d eksportu CSV: {ex.Message}", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportExcel()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "report_export.xlsx");
                var sb = new StringBuilder();
                sb.AppendLine("Category\tAmount\tSharePercent");
                foreach (var d in Details)
                    sb.AppendLine($"{d.Name}\t{d.Amount:N2}\t{d.SharePercent:N1}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Eksport Excel (TSV) zapisano na pulpicie: {path}", "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"B³¹d eksportu Excel: {ex.Message}", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdf()
        {
            try
            {
                var path = PdfExportService.ExportReportsPdf(this);
                MessageBox.Show(
                    $"Raport PDF zapisano na pulpicie:\n{path}",
                    "Eksport PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"B³¹d eksportu PDF: {ex.Message}",
                    "B³¹d eksportu PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

    }

    static class DataTableExtensions
    {
        public static DataTable CopyToDataTableOrEmpty(this IEnumerable<DataRow> rows)
        {
            var list = rows.ToList();
            if (list.Count == 0) return new DataTable();
            return list.CopyToDataTable();
        }
    }
}
