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
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using Finly.Pages; // for GoalVm

namespace Finly.ViewModels
{
    public class ReportsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ReportsViewModel()
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            _fromDate = startOfMonth;
            _toDate = endOfMonth;

            // initialize collections to be filled dynamically
            Accounts = new ObservableCollection<string>();
            Categories = new ObservableCollection<string>();
            Envelopes = new ObservableCollection<string>();
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

            // unified rows collection for KPI/exports
            Rows = new ObservableCollection<ReportsService.ReportItem>();

            // podsumowania dla zak³adki „Kategorie”
            ExpenseCategoriesSummary = new ObservableCollection<CategoryAmount>();
            IncomeCategoriesSummary = new ObservableCollection<CategoryAmount>();

            // NEW: collections for other tabs
            BudgetsSummary = new ObservableCollection<BudgetService.BudgetSummary>();
            GoalsList = new ObservableCollection<GoalVm>();
            LoansList = new ObservableCollection<LoanModel>();

            LoadAccountsAndEnvelopes();
            LoadMoneyPlaces();
        }

        // Podsumowania kategorii dla zak³adki "Kategorie"
        public ObservableCollection<CategoryAmount> ExpenseCategoriesSummary { get; }
        public ObservableCollection<CategoryAmount> IncomeCategoriesSummary { get; }

        // NEW: bud¿ety / cele / kredyty
        public ObservableCollection<BudgetService.BudgetSummary> BudgetsSummary { get; }
        public ObservableCollection<GoalVm> GoalsList { get; }
        public ObservableCollection<LoanModel> LoansList { get; }

        private void LoadAccountsAndEnvelopes()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();

                // ===== Konta bankowe =====
                BankAccounts = new ObservableCollection<BankAccountModel>();
                BankAccounts.Add(new BankAccountModel
                {
                    Id = 0,
                    UserId = uid,
                    AccountName = "Wszystkie konta bankowe",
                    BankName = ""
                });
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

        // map enum source to UI string used in DB
        private string GetSourceString()
        {
            return SelectedSource switch
            {
                SourceType.FreeCash => "Wolna gotówka",
                SourceType.SavedCash => "Od³o¿ona gotówka",
                SourceType.BankAccounts => "Konta bankowe",
                SourceType.Envelopes => "Koperty",
                _ => "Wszystko"
            };
        }

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

        // Alias properties expected by XAML
        public string CurrentPeriodLabel => CurrentPeriodName;
        public string PreviousPeriodLabel => PreviousPeriodName;

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

        // unified rows of current period
        public ObservableCollection<ReportsService.ReportItem> Rows { get; private set; }

        // Typ transakcji: wszystko / tylko wydatki / tylko przychody
        public ObservableCollection<string> TransactionTypes { get; } =
            new ObservableCollection<string>
            {
                "Wszystko",
                "Wydatki",
                "Przychody"
            };

        // pojedyncze pole dla SelectedTransactionType
        private string _selectedTransactionType = "Wszystko";
        public string SelectedTransactionType
        {
            get => _selectedTransactionType;
            set
            {
                var newValue = string.IsNullOrWhiteSpace(value) ? "Wszystko" : value;
                if (_selectedTransactionType != newValue)
                {
                    _selectedTransactionType = newValue;
                    Raise(nameof(SelectedTransactionType));
                    Refresh();
                }
            }
        }

        // Lista konkretnych miejsc: gotówka, koperty, konta bankowe
        public ObservableCollection<string> MoneyPlaces { get; } = new();

        private string _selectedMoneyPlace = "Wszystko";
        public string SelectedMoneyPlace
        {
            get => _selectedMoneyPlace;
            set
            {
                if (_selectedMoneyPlace != value)
                {
                    _selectedMoneyPlace = string.IsNullOrWhiteSpace(value)
                        ? "Wszystko"
                        : value;

                    Raise(nameof(SelectedMoneyPlace));
                    Refresh();
                }
            }
        }

        // Czy combobox z kontem/kopert¹ ma byæ aktywny
        private bool _isMoneyPlaceFilterEnabled;
        public bool IsMoneyPlaceFilterEnabled
        {
            get => _isMoneyPlaceFilterEnabled;
            set
            {
                if (_isMoneyPlaceFilterEnabled != value)
                {
                    _isMoneyPlaceFilterEnabled = value;
                    Raise(nameof(IsMoneyPlaceFilterEnabled));
                }
            }
        }

        private void LoadMoneyPlaces()
        {
            MoneyPlaces.Clear();
            MoneyPlaces.Add("Wszystko");

            // sta³e pozycje – wolna / od³o¿ona gotówka
            MoneyPlaces.Add("Wolna gotówka");
            MoneyPlaces.Add("Od³o¿ona gotówka");

            try
            {
                var uid = UserService.GetCurrentUserId();
                var envs = DatabaseService.GetEnvelopesNames(uid) ?? new List<string>();
                foreach (var env in envs)
                    MoneyPlaces.Add($"Koperta: {env}");
            }
            catch { }

            try
            {
                var uid = UserService.GetCurrentUserId();
                var accs = DatabaseService.GetAccounts(uid) ?? new List<BankAccountModel>();
                foreach (var acc in accs)
                    MoneyPlaces.Add($"Konto: {acc.AccountName}");
            }
            catch { }

            SelectedMoneyPlace = MoneyPlaces.FirstOrDefault() ?? "Wszystko";
        }

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

                    UpdateMoneyPlaceFilterState();
                    Refresh();
                }
            }
        }

        public bool ShowBankSelector => SelectedSource == SourceType.BankAccounts;
        public bool ShowEnvelopeSelector => SelectedSource == SourceType.Envelopes;

        private void UpdateMoneyPlaceFilterState()
        {
            if (SelectedSource == SourceType.BankAccounts ||
                SelectedSource == SourceType.Envelopes)
            {
                IsMoneyPlaceFilterEnabled = true;
            }
            else
            {
                IsMoneyPlaceFilterEnabled = false;
                SelectedMoneyPlace = MoneyPlaces.FirstOrDefault() ?? "Wszystko";
            }
        }

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
            SelectedTransactionType = TransactionTypes.FirstOrDefault() ?? "Wszystko";
            SelectedMoneyPlace = MoneyPlaces.FirstOrDefault() ?? "Wszystko";

            UpdateMoneyPlaceFilterState();
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

        // ====== opis œrodka wykresu (Przegl¹d) ======
        private string _selectedSliceInfo = "Kliknij kategoriê na wykresie";
        public string SelectedSliceInfo
        {
            get => _selectedSliceInfo;
            set
            {
                if (_selectedSliceInfo != value)
                {
                    _selectedSliceInfo = value;
                    Raise(nameof(SelectedSliceInfo));
                }
            }
        }

        // Metoda wo³ana z ReportsPage.xaml.cs przy najechaniu/klikniêciu na slice
        public void UpdateSelectedSliceInfo(params object[] args)
        {
            if (args == null || args.Length == 0)
            {
                SelectedSliceInfo = "Kliknij kategoriê na wykresie";
                return;
            }

            // Spróbujmy z³o¿yæ sensowny opis z przekazanych parametrów
            // (np. kategoria, kwota, udzia³ %)
            var parts = args
                .Where(a => a != null)
                .Select(a => a.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s));

            var text = string.Join(" • ", parts);
            if (string.IsNullOrWhiteSpace(text))
                text = "Kliknij kategoriê na wykresie";

            SelectedSliceInfo = text;
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

        // ======== PRZEGL¥D – œrodek donuta + mini-podsumowanie ========
        private string _overviewCenterTitle = "Wszystkie transakcje";
        public string OverviewCenterTitle
        {
            get => _overviewCenterTitle;
            set
            {
                if (_overviewCenterTitle != value)
                {
                    _overviewCenterTitle = value;
                    Raise(nameof(OverviewCenterTitle));
                }
            }
        }

        private string _overviewCenterSubtitle = string.Empty;
        public string OverviewCenterSubtitle
        {
            get => _overviewCenterSubtitle;
            set
            {
                if (_overviewCenterSubtitle != value)
                {
                    _overviewCenterSubtitle = value;
                    Raise(nameof(OverviewCenterSubtitle));
                }
            }
        }

        public string OverviewTransactionsCountStr
            => $"{_transactionsSnapshot.Count} transakcji";

        public string OverviewTotalAmountStr
        {
            get
            {
                var total = _transactionsSnapshot.Sum(t => t.Amount);
                return total.ToString("N2", CultureInfo.CurrentCulture) + " z³";
            }
        }

        public string OverviewTopCategoryStr
        {
            get
            {
                if (_transactionsSnapshot.Count == 0) return "(brak danych)";

                var top = _transactionsSnapshot
                    .GroupBy(t => t.Category ?? "(brak)")
                    .Select(g => new { Name = g.Key, Total = g.Sum(x => x.Amount) })
                    .OrderByDescending(x => Math.Abs(x.Total))
                    .FirstOrDefault();

                if (top == null) return "(brak danych)";

                return $"{top.Name} – {top.Total.ToString("N2", CultureInfo.CurrentCulture)} z³";
            }
        }

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
                Raise(nameof(ExpensesChangeStr));
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
                Raise(nameof(IncomesChangeStr));
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
                Raise(nameof(BalanceChangeStr));
            }
        }

        // Aliasy na potrzeby XAML
        public string ExpensesChangeStr => ExpensesChangePercentStr;
        public string IncomesChangeStr => IncomesChangePercentStr;
        public string BalanceChangeStr => BalanceChangePercentStr;

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
                .Select(g => new
                {
                    Name = g.Key,
                    Total = g.Sum(r => Convert.ToDecimal(r.Field<object>("Amount")))
                })
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

            // Tekst w œrodku donuta – dla wybranej kategorii
            var total = list.Sum(t => t.Amount);
            OverviewCenterTitle = category;
            OverviewCenterSubtitle = $"{list.Count} transakcji · {total.ToString("N2", CultureInfo.CurrentCulture)} z³";

            Raise(nameof(OverviewTransactionsCountStr));
            Raise(nameof(OverviewTotalAmountStr));
            Raise(nameof(OverviewTopCategoryStr));
        }

        public void BackToSummary()
        {
            IsDrilldownActive = false;
            FilteredTransactions.Clear();
            foreach (var t in _transactionsSnapshot.OrderByDescending(t => t.Date))
                FilteredTransactions.Add(t);

            // Powrót do widoku wszystkich transakcji
            OverviewCenterTitle = "Wszystkie transakcje";
            OverviewCenterSubtitle = $"{_transactionsSnapshot.Count} transakcje w wybranym okresie";

            Raise(nameof(OverviewTransactionsCountStr));
            Raise(nameof(OverviewTotalAmountStr));
            Raise(nameof(OverviewTopCategoryStr));
        }

        // ==================== G£ÓWNE ODŒWIE¯ANIE ====================
        private void Refresh()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                if (uid <= 0)
                {
                    MessageBox.Show("Brak zalogowanego u¿ytkownika.", "B³¹d",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int? accountId = null;
                if (SelectedSource == SourceType.BankAccounts &&
                    SelectedBankAccount != null &&
                    SelectedBankAccount.Id > 0)
                {
                    accountId = SelectedBankAccount.Id;
                }

                // unified wiersze dla bie¿¹cego okresu
                var currentRows = ReportsService.LoadReport(
                    uid,
                    GetSourceString(),
                    SelectedCategory,
                    SelectedTransactionType,
                    SelectedMoneyPlace,
                    FromDate,
                    ToDate
                ).ToList();

                // ====== aktualny okres – DataTable z wydatków do wykresu ======
                var currentDt = GetFilteredExpensesDataTable(uid, FromDate, ToDate, accountId);

                Rows.Clear();
                foreach (var row in currentRows)
                    Rows.Add(row);

                RebuildCategorySummariesFromRows();

                var totalExpenses = currentRows.Where(r => r.Amount < 0m).Sum(r => -r.Amount);
                var totalIncomes = currentRows.Where(r => r.Amount > 0m).Sum(r => r.Amount);

                ExpensesTotal = totalExpenses;
                IncomesTotal = totalIncomes;
                BalanceTotal = totalIncomes - totalExpenses;

                // ====== poprzedni okres o tej samej d³ugoœci ======
                var prev = GetPreviousPeriod(FromDate, ToDate);
                _previousFromDate = prev.PrevFrom;
                _previousToDate = prev.PrevTo;

                var previousRows = ReportsService.LoadReport(
                    uid,
                    GetSourceString(),
                    SelectedCategory,
                    SelectedTransactionType,
                    SelectedMoneyPlace,
                    _previousFromDate,
                    _previousToDate
                ).ToList();

                PreviousExpensesTotal = previousRows.Where(r => r.Amount < 0m).Sum(r => -r.Amount);
                PreviousIncomesTotal = previousRows.Where(r => r.Amount > 0m).Sum(r => r.Amount);
                PreviousBalanceTotal = PreviousIncomesTotal - PreviousExpensesTotal;

                var kind = DetectPeriodKind(FromDate.Date, ToDate.Date, DateTime.Today);
                var (currentName, previousName) = GetPeriodNames(kind);
                CurrentPeriodName = currentName;
                PreviousPeriodName = previousName;

                AnalyzedPeriodLabel = $"{CurrentPeriodName} ({FromDate:dd.MM.yyyy} – {ToDate:dd.MM.yyyy})";
                ComparisonPeriodLabel = $"{PreviousPeriodName} ({prev.PrevFrom:dd.MM.yyyy} – {prev.PrevTo:dd.MM.yyyy})";

                // ====== ró¿nice procentowe ======
                ExpensesChangePercentStr = FormatPercentChange(PreviousExpensesTotal, ExpensesTotal);
                IncomesChangePercentStr = FormatPercentChange(PreviousIncomesTotal, IncomesTotal);
                BalanceChangePercentStr = FormatPercentChange(PreviousBalanceTotal, BalanceTotal);

                IsDrilldownActive = false;
                SelectedSliceInfo = "Kliknij kategoriê na wykresie";

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
                Insights.Add($"Filtrowanie: Ÿród³o = {SelectedSource}, typ transakcji = {SelectedTransactionType}, miejsce = {SelectedMoneyPlace}");
                Insights.Add($"Analizowany okres: {AnalyzedPeriodLabel}");
                Insights.Add($"Porównanie z okresem: {ComparisonPeriodLabel}");
                Insights.Add($"Wydajesz {ExpensesChangePercentStr} {(ExpensesTotal > PreviousExpensesTotal ? "wiêcej" : ExpensesTotal < PreviousExpensesTotal ? "mniej" : "(bez zmian)")} ni¿ w {PreviousPeriodName}.");
                Insights.Add($"Twoje przychody s¹ {IncomesChangePercentStr} {(IncomesTotal > PreviousIncomesTotal ? "wy¿sze" : IncomesTotal < PreviousIncomesTotal ? "ni¿sze" : "(bez zmian)")} ni¿ w {PreviousPeriodName}.");

                // ====== wykres i szczegó³y kategorii – z wydatków ======
                var currentExpensesDt = GetFilteredExpensesDataTable(uid, FromDate, ToDate, accountId);
                FilteredTransactions.Clear();
                Details.Clear();
                if (currentExpensesDt.Rows.Count > 0)
                    PopulateFromDataTable(currentExpensesDt);
                else
                {
                    ChartTotals = new Dictionary<string, decimal>();
                    ChartTotalAll = 0m;
                }

                OverviewCenterTitle = "Wszystkie transakcje";
                OverviewCenterSubtitle = $"{_transactionsSnapshot.Count} transakcji w wybranym okresie";

                Raise(nameof(Details));
                Raise(nameof(ChartTotals));
                Raise(nameof(ChartTotalAll));
                Raise(nameof(FilteredTransactions));
                Raise(nameof(OverviewTransactionsCountStr));
                Raise(nameof(OverviewTotalAmountStr));
                Raise(nameof(OverviewTopCategoryStr));

                // ====== LOAD Budgets / Goals / Loans for other tabs =====
                try
                {
                    BudgetsSummary.Clear();
                    var budgets = BudgetService.GetBudgetsWithSummary(uid) ?? new List<BudgetService.BudgetSummary>();
                    foreach (var b in budgets)
                        BudgetsSummary.Add(b);
                }
                catch { }

                try
                {
                    GoalsList.Clear();

                    // If shared GoalsService is empty, try to load envelope goals from DB
                    if (GoalsService.Goals == null || GoalsService.Goals.Count == 0)
                    {
                        try
                        {
                            var envGoals = DatabaseService.GetEnvelopeGoals(uid);
                            if (envGoals != null)
                            {
                                foreach (var g in envGoals)
                                {
                                    var goalTitle = string.Empty;
                                    var description = string.Empty;
                                    try
                                    {
                                        var raw = (g.GoalText as string) ?? string.Empty;
                                        var lines = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                        var descLines = new List<string>();
                                        foreach (var rawLine in lines)
                                        {
                                            var line = rawLine.Trim();
                                            if (line.StartsWith("Cel:", StringComparison.OrdinalIgnoreCase))
                                                goalTitle = line.Substring(4).Trim();
                                            else if (line.StartsWith("Termin:", StringComparison.OrdinalIgnoreCase))
                                                continue;
                                            else
                                                descLines.Add(line);
                                        }
                                        if (string.IsNullOrWhiteSpace(goalTitle)) goalTitle = g.Name ?? string.Empty;
                                        description = string.Join(Environment.NewLine, descLines);
                                    }
                                    catch { }

                                    var vm = new GoalVm
                                    {
                                        EnvelopeId = g.EnvelopeId,
                                        Name = g.Name ?? string.Empty,
                                        GoalTitle = goalTitle,
                                        TargetAmount = g.Target,
                                        CurrentAmount = g.Allocated,
                                        DueDate = g.Deadline,
                                        Description = description
                                    };

                                    GoalsService.Goals.Add(vm);
                                }
                            }
                        }
                        catch { }
                    }

                    foreach (var g in GoalsService.Goals)
                        GoalsList.Add(g);
                }
                catch { }

                try
                {
                    LoansList.Clear();
                    LoansViewList.Clear();

                    var loans = DatabaseService.GetLoans(uid) ?? new List<LoanModel>();
                    foreach (var l in loans)
                        LoansList.Add(l);

                    foreach (var l in loans)
                    {
                        var monthly = LoansService.CalculateMonthlyPayment(l.Principal, l.InterestRate, l.TermMonths);
                        string nextInfo = monthly.ToString("N2", CultureInfo.CurrentCulture) + " z³";
                        try
                        {
                            var today = DateTime.Today;
                            DateTime nextDate;
                            if (l.PaymentDay <= 0)
                                nextDate = l.StartDate.AddMonths(1);
                            else
                            {
                                int daysInThisMonth = DateTime.DaysInMonth(today.Year, today.Month);
                                int day = Math.Min(l.PaymentDay, daysInThisMonth);
                                var candidate = new DateTime(today.Year, today.Month, day);
                                if (candidate <= today)
                                {
                                    var nextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                                    day = Math.Min(l.PaymentDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                                    candidate = new DateTime(nextMonth.Year, nextMonth.Month, day);
                                }
                                nextDate = candidate;
                            }
                            nextInfo += " · " + nextDate.ToString("dd.MM.yyyy");
                        }
                        catch { }

                        LoansViewList.Add(new ReportsLoanVm
                        {
                            Id = l.Id,
                            Name = l.Name,
                            Principal = l.Principal,
                            InterestRate = l.InterestRate,
                            TermMonths = l.TermMonths,
                            MonthlyPaymentStr = monthly.ToString("N2", CultureInfo.CurrentCulture) + " z³",
                            NextPaymentInfo = nextInfo
                        });
                    }
                }
                catch { }

                // If some sections are empty (no DB data), try to load missing data again from DB
                FillMissingFromDatabase(uid);

                // If still empty, populate demo/sample data so UI looks informative in dev mode
                EnsureDemoData(uid);

                Raise(nameof(BudgetsSummary));
                Raise(nameof(GoalsList));
                Raise(nameof(LoansList));
                Raise(nameof(LoansViewList));
            }
            catch (Exception ex)
            {
                MessageBox.Show("B³¹d odczytu raportu: " + ex.Message,
                    "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Try to fill missing collections from database (called before falling back to demo data)
        private void FillMissingFromDatabase(int uid)
        {
            try
            {
                if (uid <= 0) return;

                if (BudgetsSummary.Count == 0)
                {
                    try
                    {
                        var budgets = BudgetService.GetBudgetsWithSummary(uid) ?? new List<BudgetService.BudgetSummary>();
                        foreach (var b in budgets)
                            BudgetsSummary.Add(b);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase Budgets error: " + ex.Message);
                    }
                }

                if (GoalsList.Count == 0)
                {
                    try
                    {
                        var envGoals = DatabaseService.GetEnvelopeGoals(uid);
                        if (envGoals != null)
                        {
                            foreach (var g in envGoals)
                            {
                                try
                                {
                                    var vm = new GoalVm
                                    {
                                        EnvelopeId = g.EnvelopeId,
                                        Name = g.Name ?? string.Empty,
                                        GoalTitle = (g.GoalText as string) ?? string.Empty,
                                        TargetAmount = g.Target,
                                        CurrentAmount = g.Allocated,
                                        DueDate = g.Deadline,
                                        Description = g.GoalText as string ?? string.Empty
                                    };
                                    GoalsList.Add(vm);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase Goals item error: " + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase Goals error: " + ex.Message);
                    }
                }

                if (LoansViewList.Count == 0)
                {
                    try
                    {
                        var loans = DatabaseService.GetLoans(uid) ?? new List<LoanModel>();
                        foreach (var l in loans)
                        {
                            var monthly = LoansService.CalculateMonthlyPayment(l.Principal, l.InterestRate, l.TermMonths);
                            string nextInfo = monthly.ToString("N2", CultureInfo.CurrentCulture) + " z³";
                            try
                            {
                                var today = DateTime.Today;
                                DateTime nextDate;
                                if (l.PaymentDay <= 0)
                                    nextDate = l.StartDate.AddMonths(1);
                                else
                                {
                                    int daysInThisMonth = DateTime.DaysInMonth(today.Year, today.Month);
                                    int day = Math.Min(l.PaymentDay, daysInThisMonth);
                                    var candidate = new DateTime(today.Year, today.Month, day);
                                    if (candidate <= today)
                                    {
                                        var nextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                                        day = Math.Min(l.PaymentDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                                        candidate = new DateTime(nextMonth.Year, nextMonth.Month, day);
                                    }
                                    nextDate = candidate;
                                }
                                nextInfo += " · " + nextDate.ToString("dd.MM.yyyy");
                            }
                            catch { }

                            LoansViewList.Add(new ReportsLoanVm
                            {
                                Id = l.Id,
                                Name = l.Name,
                                Principal = l.Principal,
                                InterestRate = l.InterestRate,
                                TermMonths = l.TermMonths,
                                MonthlyPaymentStr = monthly.ToString("N2", CultureInfo.CurrentCulture) + " z³",
                                NextPaymentInfo = nextInfo
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase Loans error: " + ex.Message);
                    }
                }

                // Ensure Envelopes (filter) is populated
                try
                {
                    var envs = DatabaseService.GetEnvelopesNames(uid) ?? new List<string>();
                    Envelopes.Clear();
                    Envelopes.Add("Wszystkie koperty");
                    foreach (var e in envs)
                        Envelopes.Add(e);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase Envelopes error: " + ex.Message);
                }

                // Ensure Categories & Accounts
                try
                {
                    var cats = DatabaseService.GetCategoriesByUser(uid) ?? new List<string>();
                    Categories.Clear();
                    Categories.Add("Wszystkie kategorie");
                    foreach (var c in cats)
                        Categories.Add(c);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase Categories error: " + ex.Message);
                }

                try
                {
                    BankAccounts.Clear();
                    BankAccounts.Add(new BankAccountModel { Id = 0, UserId = uid, AccountName = "Wszystkie konta bankowe", BankName = "" });
                    foreach (var a in DatabaseService.GetAccounts(uid) ?? new List<BankAccountModel>())
                        BankAccounts.Add(a);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase Accounts error: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("FillMissingFromDatabase general error: " + ex.Message);
            }
        }

        private void SavePreset()
        {
            MessageBox.Show($"Zapisano preset: {SelectedTemplate}",
                "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportCsv()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "report_export.csv");

                var sb = new StringBuilder();
                sb.AppendLine("Category,Amount,SharePercent");
                foreach (var d in Details)
                    sb.AppendLine($"{d.Name},{d.Amount:N2},{d.SharePercent:N1}");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Eksport CSV zapisano na pulpicie: {path}",
                    "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"B³¹d eksportu CSV: {ex.Message}",
                    "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportExcel()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "report_export.xlsx");

                var sb = new StringBuilder();
                sb.AppendLine("Category\tAmount\tSharePercent");
                foreach (var d in Details)
                    sb.AppendLine($"{d.Name}\t{d.Amount:N2}\t{d.SharePercent:N1}");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Eksport Excel (TSV) zapisano na pulpicie: {path}",
                    "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"B³¹d eksportu Excel: {ex.Message}",
                    "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdf()
        {
            try
            {
                var path = PdfExportService.ExportReportsPdf(this);
                ToastService.Success($"Raport PDF zapisano na pulpicie: {path}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"B³¹d eksportu PDF: {ex.Message}");
            }
        }

        // ======= pomocnicze: budowanie podsumowañ kategorii z Rows =======
        private void RebuildCategorySummariesFromRows()
        {
            ExpenseCategoriesSummary.Clear();
            IncomeCategoriesSummary.Clear();

            if (Rows == null || Rows.Count == 0)
                return;

            // Wydatki wed³ug kategorii
            var expenseGroups = Rows
                .Where(r => r.Amount < 0m)
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category);

            decimal totalExpenses = expenseGroups.Sum(g => -g.Sum(x => x.Amount));

            foreach (var g in expenseGroups.OrderByDescending(g => -g.Sum(x => x.Amount)))
            {
                var sum = -g.Sum(x => x.Amount);
                var share = totalExpenses > 0m ? (double)(sum / totalExpenses * 100m) : 0.0;

                ExpenseCategoriesSummary.Add(new CategoryAmount
                {
                    Name = g.Key,
                    Amount = sum,
                    SharePercent = share
                });
            }

            // Przychody wed³ug kategorii
            var incomeGroups = Rows
                .Where(r => r.Amount > 0m)
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category);

            decimal totalIncomes = incomeGroups.Sum(g => g.Sum(x => x.Amount));

            foreach (var g in incomeGroups.OrderByDescending(g => g.Sum(x => x.Amount)))
            {
                var sum = g.Sum(x => x.Amount);
                var share = totalIncomes > 0m ? (double)(sum / totalIncomes * 100m) : 0.0;

                IncomeCategoriesSummary.Add(new CategoryAmount
                {
                    Name = g.Key,
                    Amount = sum,
                    SharePercent = share
                });
            }

            Raise(nameof(ExpenseCategoriesSummary));
            Raise(nameof(IncomeCategoriesSummary));
        }

        // ====== poprzedni okres o tej samej d³ugoœci ======
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

        // ====== filtrowanie wydatków do wykresu ======
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
                if (!string.IsNullOrWhiteSpace(SelectedEnvelope) &&
                    SelectedEnvelope != "Wszystkie koperty")
                {
                    rows = rows.Where(r =>
                        (r.Field<string>("Description") ?? "")
                        .IndexOf(SelectedEnvelope, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else
                {
                    rows = rows.Where(r => r.IsNull("AccountId"));
                }
            }

            return rows.CopyToDataTableOrEmpty();
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

        // ====== Zak³adki raportów (indeks) ======
        private int _selectedTabIndex = 0;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    Raise(nameof(SelectedTabIndex));
                    Raise(nameof(IsOverviewTab));
                    Raise(nameof(IsCategoriesTab));
                    // Removed direct Refresh() call to avoid exceptions during tab switch.
                }
            }
        }

        public bool IsOverviewTab => SelectedTabIndex == 0;
        public bool IsCategoriesTab => SelectedTabIndex == 1;

        // NEW: pretty view model for loans shown in Reports page
        public class ReportsLoanVm
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Principal { get; set; }
            public decimal InterestRate { get; set; }
            public int TermMonths { get; set; }
            public string PrincipalStr => Principal.ToString("N2", CultureInfo.CurrentCulture) + " z³";
            public string InterestRateStr => InterestRate.ToString("N2", CultureInfo.CurrentCulture) + " %";
            public string MonthlyPaymentStr { get; set; } = "0,00 z³";
            public string NextPaymentInfo { get; set; } = "-";
        }

        public ObservableCollection<ReportsLoanVm> LoansViewList { get; } = new();

        // NEW: fallback demo data if some DB collections are empty
        private void EnsureDemoData(int uid)
        {
            // Basic demo data for empty setup
            try
            {
                // Budgets
                if (BudgetsSummary.Count == 0)
                {
                    BudgetsSummary.Add(new BudgetService.BudgetSummary
                    {
                        Id = -1,
                        Name = "Domowy bud¿et",
                        Type = "Miesiêczny",
                        StartDate = DateTime.Today.AddMonths(-1),
                        EndDate = DateTime.Today.AddMonths(1),
                        PlannedAmount = 5000m,
                        Spent = 3120.45m,
                        IncomesForBudget = 0m
                    });
                }

                // Goals
                if (GoalsList.Count == 0)
                {
                    GoalsList.Add(new GoalVm { EnvelopeId = -1, Name = "Wakacje", GoalTitle = "Wakacje", TargetAmount = 6000m, CurrentAmount = 1500m, DueDate = DateTime.Today.AddMonths(12), Description = "Demo" });
                }

                // Loans view
                if (LoansViewList.Count == 0)
                {
                    LoansViewList.Add(new ReportsLoanVm { Id = -1, Name = "Kredyt demo", Principal = 250000m, InterestRate = 3.6m, TermMonths = 360, MonthlyPaymentStr = LoansService.CalculateMonthlyPayment(250000m, 3.6m, 360).ToString("N2", CultureInfo.CurrentCulture) + " z³", NextPaymentInfo = DateTime.Today.AddDays(14).ToString("dd.MM.yyyy") });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EnsureDemoData error: " + ex.Message);
            }
        }

        // Public explicit loaders for individual tabs so ReportsPage can call them on demand
        public void LoadBudgets()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                BudgetsSummary.Clear();
                var budgets = BudgetService.GetBudgetsWithSummary(uid) ?? new List<BudgetService.BudgetSummary>();
                foreach (var b in budgets)
                    BudgetsSummary.Add(b);
                Raise(nameof(BudgetsSummary));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadBudgets error: " + ex.Message);
            }
        }

        public void LoadGoals()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                GoalsList.Clear();

                try
                {
                    // Load directly from DB so we don't miss goals stored in Note/GoalText
                    var envGoals = DatabaseService.GetEnvelopeGoals(uid) ?? new List<DatabaseService.EnvelopeGoalDto>();

                    // also refresh shared cache
                    GoalsService.Goals.Clear();

                    foreach (var g in envGoals)
                    {
                        try
                        {
                            var raw = (g.GoalText as string) ?? string.Empty;
                            var goalTitle = string.Empty;
                            var descLines = new List<string>();
                            var lines = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var rawLine in lines)
                            {
                                var line = rawLine.Trim();
                                if (line.StartsWith("Cel:", StringComparison.OrdinalIgnoreCase))
                                    goalTitle = line.Substring(4).Trim();
                                else if (line.StartsWith("Termin:", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                else
                                    descLines.Add(line);
                            }
                            if (string.IsNullOrWhiteSpace(goalTitle)) goalTitle = g.Name ?? string.Empty;

                            var vm = new GoalVm
                            {
                                EnvelopeId = g.EnvelopeId,
                                Name = g.Name ?? string.Empty,
                                GoalTitle = goalTitle,
                                TargetAmount = g.Target,
                                CurrentAmount = g.Allocated,
                                DueDate = g.Deadline,
                                Description = string.Join(Environment.NewLine, descLines)
                            };

                            GoalsList.Add(vm);
                            GoalsService.Goals.Add(vm);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("LoadGoals item error: " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("LoadGoals error: " + ex.Message);
                }

                Raise(nameof(GoalsList));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadGoals error: " + ex.Message);
            }
        }

        public void LoadEnvelopes()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                Envelopes.Clear();
                Envelopes.Add("Wszystkie koperty");
                var envs = DatabaseService.GetEnvelopesNames(uid) ?? new List<string>();
                foreach (var e in envs)
                    Envelopes.Add(e);
                Raise(nameof(Envelopes));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadEnvelopes error: " + ex.Message);
            }
        }

        public void LoadCategoryStats()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                // rebuild expense/income summaries using current Rows if present, otherwise read fresh rows
                if (Rows == null || Rows.Count == 0)
                {
                    // load unified rows for current period
                    var currentRows = ReportsService.LoadReport(
                        uid,
                        GetSourceString(),
                        SelectedCategory,
                        SelectedTransactionType,
                        SelectedMoneyPlace,
                        FromDate,
                        ToDate
                    ).ToList();

                    Rows.Clear();
                    foreach (var row in currentRows)
                        Rows.Add(row);
                }

                RebuildCategorySummariesFromRows();
                Raise(nameof(ExpenseCategoriesSummary));
                Raise(nameof(IncomeCategoriesSummary));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadCategoryStats error: " + ex.Message);
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