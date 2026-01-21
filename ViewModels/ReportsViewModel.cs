using Finly.Helpers;
using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

namespace Finly.ViewModels
{
    public sealed class ReportsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // =========================
        // Ctor
        // =========================
        public ReportsViewModel()
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            _fromDate = startOfMonth;
            _toDate = endOfMonth;

            RecalcPreviousRange();

            // Dane
            Rows = new ObservableCollection<ReportsService.ReportItem>();
            PreviousRows = new ObservableCollection<ReportsService.ReportItem>();

            OverviewTopExpenses = new ObservableCollection<TxLine>();
            OverviewTopIncomes = new ObservableCollection<TxLine>();

            Budgets = new ObservableCollection<BudgetRow>();
            Loans = new ObservableCollection<LoanRow>();
            Goals = new ObservableCollection<GoalRow>();
            Investments = new ObservableCollection<InvestmentRow>();
            PlannedSim = new ObservableCollection<PlannedRow>();

            // Komendy
            RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
            ExportPdfCommand = new RelayCommand(param => ExportPdf(param?.ToString()));

            // Startowe serie/osi
            InitAllCharts();
        }

        // =========================
        // Debounce / re-entrancy
        // =========================
        private int _refreshing;

        public async Task RefreshAsync()
        {
            if (Interlocked.Exchange(ref _refreshing, 1) == 1)
                return;

            try
            {
                var uid = UserService.GetCurrentUserId();
                if (uid <= 0)
                    throw new InvalidOperationException("Brak zalogowanego użytkownika.");

                var from = FromDate;
                var to = ToDate;
                var pFrom = PreviousFrom;
                var pTo = PreviousTo;

                var snap = await Task.Run(() => BuildSnapshot(uid, from, to, pFrom, pTo));
                ApplySnapshot(snap);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        // =========================
        // Daty (PeriodBarControl)
        // =========================
        private DateTime _fromDate;
        public DateTime FromDate
        {
            get => _fromDate;
            set
            {
                var v = value.Date;
                if (_fromDate != v)
                {
                    _fromDate = v;
                    Raise(nameof(FromDate));
                    Raise(nameof(PeriodLabel));
                    RecalcPreviousRange();
                }
            }
        }

        private DateTime _toDate;
        public DateTime ToDate
        {
            get => _toDate;
            set
            {
                var v = value.Date;
                if (_toDate != v)
                {
                    _toDate = v;
                    Raise(nameof(ToDate));
                    Raise(nameof(PeriodLabel));
                    RecalcPreviousRange();
                }
            }
        }

        public string PeriodLabel => $"{FromDate:dd.MM.yyyy} – {ToDate:dd.MM.yyyy}";

        // =========================
        // Poprzedni okres
        // =========================
        private DateTime _prevFrom;
        public DateTime PreviousFrom
        {
            get => _prevFrom;
            private set
            {
                if (_prevFrom != value)
                {
                    _prevFrom = value;
                    Raise(nameof(PreviousFrom));
                    Raise(nameof(PreviousPeriodLabel));
                }
            }
        }

        private DateTime _prevTo;
        public DateTime PreviousTo
        {
            get => _prevTo;
            private set
            {
                if (_prevTo != value)
                {
                    _prevTo = value;
                    Raise(nameof(PreviousTo));
                    Raise(nameof(PreviousPeriodLabel));
                }
            }
        }

        public string PreviousPeriodLabel => $"{PreviousFrom:dd.MM.yyyy} – {PreviousTo:dd.MM.yyyy}";

        private void RecalcPreviousRange()
        {
            var len = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);
            var prevTo = FromDate.Date.AddDays(-1);
            var prevFrom = prevTo.AddDays(-(len - 1));

            PreviousFrom = prevFrom;
            PreviousTo = prevTo;
        }

        // =========================
        // Komendy / Zakładki
        // =========================
        public ICommand RefreshCommand { get; }
        public ICommand ExportPdfCommand { get; }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    Raise(nameof(SelectedTabIndex));
                }
            }
        }

        // =========================
        // Przegląd: filtr listy transakcji (Toggle)
        // =========================
        public enum TxFilter
        {
            All,
            Expenses,
            Incomes,
            Transfers
        }

        private TxFilter _selectedTxFilter = TxFilter.All;
        public TxFilter SelectedTxFilter
        {
            get => _selectedTxFilter;
            private set
            {
                if (_selectedTxFilter != value)
                {
                    _selectedTxFilter = value;
                    Raise(nameof(SelectedTxFilter));
                    Raise(nameof(FilterAll));
                    Raise(nameof(FilterExpenses));
                    Raise(nameof(FilterIncomes));
                    Raise(nameof(FilterTransfers));
                    ApplyTransactionsFilter();
                }
            }
        }

        public bool FilterAll
        {
            get => SelectedTxFilter == TxFilter.All;
            set { if (value) SelectedTxFilter = TxFilter.All; }
        }
        public bool FilterExpenses
        {
            get => SelectedTxFilter == TxFilter.Expenses;
            set { if (value) SelectedTxFilter = TxFilter.Expenses; }
        }
        public bool FilterIncomes
        {
            get => SelectedTxFilter == TxFilter.Incomes;
            set { if (value) SelectedTxFilter = TxFilter.Incomes; }
        }
        public bool FilterTransfers
        {
            get => SelectedTxFilter == TxFilter.Transfers;
            set { if (value) SelectedTxFilter = TxFilter.Transfers; }
        }

        private ICollectionView? _transactionsView;
        public ICollectionView? TransactionsView
        {
            get => _transactionsView;
            private set { _transactionsView = value; Raise(nameof(TransactionsView)); }
        }

        private void RebuildTransactionsView()
        {
            try
            {
                var view = CollectionViewSource.GetDefaultView(Rows);
                view.Filter = TxViewFilter;
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(nameof(ReportsService.ReportItem.Date), ListSortDirection.Descending));
                TransactionsView = view;
                view.Refresh();
            }
            catch
            {
                TransactionsView = null;
            }
        }

        private bool TxViewFilter(object obj)
        {
            if (obj is not ReportsService.ReportItem r) return false;

            return SelectedTxFilter switch
            {
                TxFilter.All => true,
                TxFilter.Expenses => r.Type == "Wydatek",
                TxFilter.Incomes => r.Type == "Przychód",
                TxFilter.Transfers => r.Type == "Transfer",
                _ => true
            };
        }

        private void ApplyTransactionsFilter()
        {
            try { TransactionsView?.Refresh(); }
            catch { }
        }

        // =========================
        // Dane wspólne
        // =========================
        private ObservableCollection<ReportsService.ReportItem> _rows = new();
        public ObservableCollection<ReportsService.ReportItem> Rows
        {
            get => _rows;
            private set { _rows = value; Raise(nameof(Rows)); }
        }

        private ObservableCollection<ReportsService.ReportItem> _previousRows = new();
        public ObservableCollection<ReportsService.ReportItem> PreviousRows
        {
            get => _previousRows;
            private set { _previousRows = value; Raise(nameof(PreviousRows)); }
        }

        // =========================
        // KPI
        // =========================
        private decimal _totalExpenses;
        public decimal TotalExpenses
        {
            get => _totalExpenses;
            private set
            {
                if (_totalExpenses != value)
                {
                    _totalExpenses = value;
                    Raise(nameof(TotalExpenses));
                    Raise(nameof(TotalExpensesStr));
                }
            }
        }
        public string TotalExpensesStr => Money(TotalExpenses);

        private decimal _totalIncomes;
        public decimal TotalIncomes
        {
            get => _totalIncomes;
            private set
            {
                if (_totalIncomes != value)
                {
                    _totalIncomes = value;
                    Raise(nameof(TotalIncomes));
                    Raise(nameof(TotalIncomesStr));
                }
            }
        }
        public string TotalIncomesStr => Money(TotalIncomes);

        private decimal _balance;
        public decimal Balance
        {
            get => _balance;
            private set
            {
                if (_balance != value)
                {
                    _balance = value;
                    Raise(nameof(Balance));
                    Raise(nameof(BalanceStr));
                }
            }
        }
        public string BalanceStr => Money(Balance);

        private decimal _previousTotalExpenses;
        public decimal PreviousTotalExpenses
        {
            get => _previousTotalExpenses;
            private set
            {
                if (_previousTotalExpenses != value)
                {
                    _previousTotalExpenses = value;
                    Raise(nameof(PreviousTotalExpenses));
                    Raise(nameof(PreviousTotalExpensesStr));
                }
            }
        }
        public string PreviousTotalExpensesStr => Money(PreviousTotalExpenses);

        private decimal _previousTotalIncomes;
        public decimal PreviousTotalIncomes
        {
            get => _previousTotalIncomes;
            private set
            {
                if (_previousTotalIncomes != value)
                {
                    _previousTotalIncomes = value;
                    Raise(nameof(PreviousTotalIncomes));
                    Raise(nameof(PreviousTotalIncomesStr));
                }
            }
        }
        public string PreviousTotalIncomesStr => Money(PreviousTotalIncomes);

        private decimal _previousBalance;
        public decimal PreviousBalance
        {
            get => _previousBalance;
            private set
            {
                if (_previousBalance != value)
                {
                    _previousBalance = value;
                    Raise(nameof(PreviousBalance));
                    Raise(nameof(PreviousBalanceStr));
                }
            }
        }
        public string PreviousBalanceStr => Money(PreviousBalance);

        private decimal _deltaExpenses;
        public decimal DeltaExpenses
        {
            get => _deltaExpenses;
            private set
            {
                if (_deltaExpenses != value)
                {
                    _deltaExpenses = value;
                    Raise(nameof(DeltaExpenses));
                    Raise(nameof(DeltaExpensesStr));
                }
            }
        }
        public string DeltaExpensesStr => DeltaMoney(DeltaExpenses);

        private decimal _deltaIncomes;
        public decimal DeltaIncomes
        {
            get => _deltaIncomes;
            private set
            {
                if (_deltaIncomes != value)
                {
                    _deltaIncomes = value;
                    Raise(nameof(DeltaIncomes));
                    Raise(nameof(DeltaIncomesStr));
                }
            }
        }
        public string DeltaIncomesStr => DeltaMoney(DeltaIncomes);

        private decimal _deltaBalance;
        public decimal DeltaBalance
        {
            get => _deltaBalance;
            private set
            {
                if (_deltaBalance != value)
                {
                    _deltaBalance = value;
                    Raise(nameof(DeltaBalance));
                    Raise(nameof(DeltaBalanceStr));
                }
            }
        }
        public string DeltaBalanceStr => DeltaMoney(DeltaBalance);

        private static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        private static string DeltaMoney(decimal v)
        {
            var sign = v > 0 ? "+" : v < 0 ? "-" : "";
            return $"{sign}{Math.Abs(v).ToString("N2", CultureInfo.CurrentCulture)} zł";
        }

        // =========================
        // Porównanie (teksty)
        // =========================
        private string _compareExpensesText = "";
        public string CompareExpensesText
        {
            get => _compareExpensesText;
            private set { if (_compareExpensesText != value) { _compareExpensesText = value; Raise(nameof(CompareExpensesText)); } }
        }

        private string _compareIncomesText = "";
        public string CompareIncomesText
        {
            get => _compareIncomesText;
            private set { if (_compareIncomesText != value) { _compareIncomesText = value; Raise(nameof(CompareIncomesText)); } }
        }

        private string _compareBalanceText = "";
        public string CompareBalanceText
        {
            get => _compareBalanceText;
            private set { if (_compareBalanceText != value) { _compareBalanceText = value; Raise(nameof(CompareBalanceText)); } }
        }

        // =========================
        // Modele tabel
        // =========================
        public sealed class TxLine
        {
            public DateTime Date { get; set; }
            public string Category { get; set; } = "";
            public decimal Amount { get; set; }
            public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        // Podium: łatwe bindy (2–1–3)
        private TxLine? _topExp1;
        public TxLine? TopExp1 { get => _topExp1; private set { _topExp1 = value; Raise(nameof(TopExp1)); } }

        private TxLine? _topExp2;
        public TxLine? TopExp2 { get => _topExp2; private set { _topExp2 = value; Raise(nameof(TopExp2)); } }

        private TxLine? _topExp3;
        public TxLine? TopExp3 { get => _topExp3; private set { _topExp3 = value; Raise(nameof(TopExp3)); } }

        private TxLine? _topInc1;
        public TxLine? TopInc1 { get => _topInc1; private set { _topInc1 = value; Raise(nameof(TopInc1)); } }

        private TxLine? _topInc2;
        public TxLine? TopInc2 { get => _topInc2; private set { _topInc2 = value; Raise(nameof(TopInc2)); } }

        private TxLine? _topInc3;
        public TxLine? TopInc3 { get => _topInc3; private set { _topInc3 = value; Raise(nameof(TopInc3)); } }

        private static TxLine? GetAt(List<TxLine> list, int index)
            => (index >= 0 && index < list.Count) ? list[index] : null;

        public ObservableCollection<TxLine> OverviewTopExpenses { get; }
        public ObservableCollection<TxLine> OverviewTopIncomes { get; }

        // =========================
        // Budżety
        // =========================
        public sealed class BudgetRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Planned { get; set; }
            public decimal Spent { get; set; }

            public bool IsOver => Spent > Planned;
            public decimal Delta => Spent - Planned;
            public decimal Remaining => Planned - Spent;
            public decimal UsedPercent => Planned <= 0 ? 0 : (Spent / Planned * 100m);

            public string PlannedStr => Money(Planned);
            public string SpentStr => Money(Spent);
            public string DeltaStr => DeltaMoney(Delta);
            public string RemainingStr => Money(Remaining);
            public string UsedPercentStr => UsedPercent.ToString("N1", CultureInfo.CurrentCulture) + " %";
        }

        public ObservableCollection<BudgetRow> Budgets { get; }

        private int _budgetsOkCount;
        public int BudgetsOkCount
        {
            get => _budgetsOkCount;
            private set { if (_budgetsOkCount != value) { _budgetsOkCount = value; Raise(nameof(BudgetsOkCount)); } }
        }

        private int _budgetsOverCount;
        public int BudgetsOverCount
        {
            get => _budgetsOverCount;
            private set { if (_budgetsOverCount != value) { _budgetsOverCount = value; Raise(nameof(BudgetsOverCount)); } }
        }

        private decimal _budgetsOverTotal;
        public decimal BudgetsOverTotal
        {
            get => _budgetsOverTotal;
            private set
            {
                if (_budgetsOverTotal != value)
                {
                    _budgetsOverTotal = value;
                    Raise(nameof(BudgetsOverTotal));
                    Raise(nameof(BudgetsOverTotalStr));
                }
            }
        }
        public string BudgetsOverTotalStr => Money(BudgetsOverTotal);

        // =========================
        // Inwestycje / Kredyty / Cele / Symulacja
        // =========================
        public sealed class InvestmentRow
        {
            public string Name { get; set; } = "";
            public decimal StartValue { get; set; }
            public decimal EndValue { get; set; }

            public decimal Profit => EndValue - StartValue;
            public decimal RoiPercent => StartValue <= 0 ? 0 : (Profit / StartValue * 100m);

            public string StartValueStr => Money(StartValue);
            public string EndValueStr => Money(EndValue);
            public string ProfitStr => Money(Profit);
            public string RoiPercentStr => RoiPercent.ToString("N1", CultureInfo.CurrentCulture) + " %";
        }
        public ObservableCollection<InvestmentRow> Investments { get; }

        public sealed class LoanRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";

            public decimal RemainingToPay { get; set; }
            public decimal PaidInPeriod { get; set; }
            public decimal OverpaidInPeriod { get; set; }

            public string RemainingToPayStr => Money(RemainingToPay);
            public string PaidInPeriodStr => Money(PaidInPeriod);
            public string OverpaidInPeriodStr => Money(OverpaidInPeriod);
        }
        public ObservableCollection<LoanRow> Loans { get; }

        public sealed class GoalRow
        {
            public string Name { get; set; } = "";
            public decimal Target { get; set; }
            public decimal Current { get; set; }
            public DateTime? DueDate { get; set; }

            public decimal ProgressPercent => Target <= 0 ? 0 : (Current / Target * 100m);
            public decimal Missing => Math.Max(0, Target - Current);

            public decimal NeededNextPeriod { get; set; }

            public string TargetStr => Money(Target);
            public string CurrentStr => Money(Current);
            public string MissingStr => Money(Missing);
            public string ProgressPercentStr => ProgressPercent.ToString("N1", CultureInfo.CurrentCulture) + " %";
            public string NeededNextPeriodStr => Money(NeededNextPeriod);
        }
        public ObservableCollection<GoalRow> Goals { get; }

        public sealed class PlannedRow
        {
            public DateTime Date { get; set; }
            public string Type { get; set; } = "";
            public string Description { get; set; } = "";
            public decimal Amount { get; set; }
            public string AmountStr => Money(Amount);
        }
        public ObservableCollection<PlannedRow> PlannedSim { get; }

        private decimal _simBalanceDelta;
        public decimal SimBalanceDelta
        {
            get => _simBalanceDelta;
            private set
            {
                if (_simBalanceDelta != value)
                {
                    _simBalanceDelta = value;
                    Raise(nameof(SimBalanceDelta));
                    Raise(nameof(SimBalanceDeltaStr));
                }
            }
        }
        public string SimBalanceDeltaStr => Money(SimBalanceDelta);

        // =========================
        // LIVECHARTS – bindowanie pod XAML
        // =========================
        private ISeries[] _overviewCountSeries = Array.Empty<ISeries>();
        public ISeries[] OverviewCountSeries { get => _overviewCountSeries; private set { _overviewCountSeries = value; Raise(nameof(OverviewCountSeries)); } }

        private Axis[] _overviewCountAxesX = Array.Empty<Axis>();
        public Axis[] OverviewCountAxesX { get => _overviewCountAxesX; private set { _overviewCountAxesX = value; Raise(nameof(OverviewCountAxesX)); } }

        private Axis[] _overviewCountAxesY = Array.Empty<Axis>();
        public Axis[] OverviewCountAxesY { get => _overviewCountAxesY; private set { _overviewCountAxesY = value; Raise(nameof(OverviewCountAxesY)); } }

        private ISeries[] _overviewAmountSeries = Array.Empty<ISeries>();
        public ISeries[] OverviewAmountSeries { get => _overviewAmountSeries; private set { _overviewAmountSeries = value; Raise(nameof(OverviewAmountSeries)); } }

        private Axis[] _overviewAmountAxesX = Array.Empty<Axis>();
        public Axis[] OverviewAmountAxesX { get => _overviewAmountAxesX; private set { _overviewAmountAxesX = value; Raise(nameof(OverviewAmountAxesX)); } }

        private Axis[] _overviewAmountAxesY = Array.Empty<Axis>();
        public Axis[] OverviewAmountAxesY { get => _overviewAmountAxesY; private set { _overviewAmountAxesY = value; Raise(nameof(OverviewAmountAxesY)); } }

        private ISeries[] _budgetsSeries = Array.Empty<ISeries>();
        public ISeries[] BudgetsSeries { get => _budgetsSeries; private set { _budgetsSeries = value; Raise(nameof(BudgetsSeries)); } }

        private Axis[] _budgetsAxesX = Array.Empty<Axis>();
        public Axis[] BudgetsAxesX { get => _budgetsAxesX; private set { _budgetsAxesX = value; Raise(nameof(BudgetsAxesX)); } }

        private Axis[] _budgetsAxesY = Array.Empty<Axis>();
        public Axis[] BudgetsAxesY { get => _budgetsAxesY; private set { _budgetsAxesY = value; Raise(nameof(BudgetsAxesY)); } }

        // NOWY: Budgets usage (%)
        private ISeries[] _budgetsUsageSeries = Array.Empty<ISeries>();
        public ISeries[] BudgetsUsageSeries { get => _budgetsUsageSeries; private set { _budgetsUsageSeries = value; Raise(nameof(BudgetsUsageSeries)); } }

        private Axis[] _budgetsUsageAxesX = Array.Empty<Axis>();
        public Axis[] BudgetsUsageAxesX { get => _budgetsUsageAxesX; private set { _budgetsUsageAxesX = value; Raise(nameof(BudgetsUsageAxesX)); } }

        private Axis[] _budgetsUsageAxesY = Array.Empty<Axis>();
        public Axis[] BudgetsUsageAxesY { get => _budgetsUsageAxesY; private set { _budgetsUsageAxesY = value; Raise(nameof(BudgetsUsageAxesY)); } }

        private ISeries[] _investmentsSeries = Array.Empty<ISeries>();
        public ISeries[] InvestmentsSeries { get => _investmentsSeries; private set { _investmentsSeries = value; Raise(nameof(InvestmentsSeries)); } }

        private Axis[] _investmentsAxesX = Array.Empty<Axis>();
        public Axis[] InvestmentsAxesX { get => _investmentsAxesX; private set { _investmentsAxesX = value; Raise(nameof(InvestmentsAxesX)); } }

        private Axis[] _investmentsAxesY = Array.Empty<Axis>();
        public Axis[] InvestmentsAxesY { get => _investmentsAxesY; private set { _investmentsAxesY = value; Raise(nameof(InvestmentsAxesY)); } }

        private ISeries[] _expenseDonutSeries = Array.Empty<ISeries>();
        public ISeries[] ExpenseDonutSeries { get => _expenseDonutSeries; private set { _expenseDonutSeries = value; Raise(nameof(ExpenseDonutSeries)); } }

        private ISeries[] _incomeDonutSeries = Array.Empty<ISeries>();
        public ISeries[] IncomeDonutSeries { get => _incomeDonutSeries; private set { _incomeDonutSeries = value; Raise(nameof(IncomeDonutSeries)); } }

        private ISeries[] _transferDonutSeries = Array.Empty<ISeries>();
        public ISeries[] TransferDonutSeries { get => _transferDonutSeries; private set { _transferDonutSeries = value; Raise(nameof(TransferDonutSeries)); } }

        private ISeries[] _loansSeries = Array.Empty<ISeries>();
        public ISeries[] LoansSeries { get => _loansSeries; private set { _loansSeries = value; Raise(nameof(LoansSeries)); } }

        private Axis[] _loansAxesX = Array.Empty<Axis>();
        public Axis[] LoansAxesX { get => _loansAxesX; private set { _loansAxesX = value; Raise(nameof(LoansAxesX)); } }

        private Axis[] _loansAxesY = Array.Empty<Axis>();
        public Axis[] LoansAxesY { get => _loansAxesY; private set { _loansAxesY = value; Raise(nameof(LoansAxesY)); } }

        private ISeries[] _goalsSeries = Array.Empty<ISeries>();
        public ISeries[] GoalsSeries { get => _goalsSeries; private set { _goalsSeries = value; Raise(nameof(GoalsSeries)); } }

        private Axis[] _goalsAxesX = Array.Empty<Axis>();
        public Axis[] GoalsAxesX { get => _goalsAxesX; private set { _goalsAxesX = value; Raise(nameof(GoalsAxesX)); } }

        private Axis[] _goalsAxesY = Array.Empty<Axis>();
        public Axis[] GoalsAxesY { get => _goalsAxesY; private set { _goalsAxesY = value; Raise(nameof(GoalsAxesY)); } }

        private ISeries[] _simulationSeries = Array.Empty<ISeries>();
        public ISeries[] SimulationSeries { get => _simulationSeries; private set { _simulationSeries = value; Raise(nameof(SimulationSeries)); } }

        private Axis[] _simulationAxesX = Array.Empty<Axis>();
        public Axis[] SimulationAxesX { get => _simulationAxesX; private set { _simulationAxesX = value; Raise(nameof(SimulationAxesX)); } }

        private Axis[] _simulationAxesY = Array.Empty<Axis>();
        public Axis[] SimulationAxesY { get => _simulationAxesY; private set { _simulationAxesY = value; Raise(nameof(SimulationAxesY)); } }

        private void InitAllCharts()
        {
            var txLabels = new[] { "Wydatki", "Przychody", "Transfery" };

            // Overview: COUNT
            OverviewCountAxesX = new[] { CreateAxisX(txLabels) };
            OverviewCountAxesY = new[] { CreateAxisY(min: 0) };
            OverviewCountSeries = new ISeries[]
            {
                new ColumnSeries<double?> { Name = "Ilość", Values = new double?[] { null, null, null } }
            };

            // Overview: AMOUNT
            OverviewAmountAxesX = new[] { CreateAxisX(txLabels) };
            OverviewAmountAxesY = new[] { CreateAxisY(min: 0) };
            OverviewAmountSeries = new ISeries[]
            {
                new ColumnSeries<double?> { Name = "Wartość (zł)", Values = new double?[] { null, null, null } }
            };

            // Budżety: Wydano vs Limit
            BudgetsAxesX = new[] { CreateAxisX(SafeLabels(null)) };
            BudgetsAxesY = new[] { CreateAxisY(min: 0) };
            BudgetsSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Wydano", Values = SafeValues(null) },
                new ColumnSeries<double> { Name = "Limit",  Values = SafeValues(null) }
            };

            // Budżety: Usage %
            BudgetsUsageAxesX = new[] { CreateAxisX(SafeLabels(null)) };
            BudgetsUsageAxesY = new[] { CreateAxisY(min: 0, max: 160) };
            BudgetsUsageSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Użycie (%)", Values = SafeValues(null) }
            };

            // Inwestycje
            InvestmentsAxesX = new[] { CreateAxisX(SafeLabels(null)) };
            InvestmentsAxesY = new[] { CreateAxisY(min: 0) };
            InvestmentsSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Zysk/Strata", Values = SafeValues(null) }
            };

            // Donuty
            ExpenseDonutSeries = Array.Empty<ISeries>();
            IncomeDonutSeries = Array.Empty<ISeries>();
            TransferDonutSeries = Array.Empty<ISeries>();

            // Kredyty
            LoansAxesX = new[] { CreateAxisX(SafeLabels(null)) };
            LoansAxesY = new[] { CreateAxisY(min: 0) };
            LoansSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Spłacono",  Values = SafeValues(null) },
                new ColumnSeries<double> { Name = "Nadpłaty",  Values = SafeValues(null) },
                new ColumnSeries<double> { Name = "Pozostało", Values = SafeValues(null) }
            };

            // Cele
            GoalsAxesX = new[] { CreateAxisX(SafeLabels(null)) };
            GoalsAxesY = new[] { CreateAxisY(min: 0, max: 100) };
            GoalsSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Postęp (%)", Values = SafeValues(null) }
            };

            // Symulacja
            SimulationAxesX = new[] { CreateAxisX(SafeLabels(null)) };
            SimulationAxesY = new[] { CreateAxisY(min: null) };
            SimulationSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "Saldo (symulacja)",
                    Values = SafeValues(null),
                    GeometrySize = 0
                }
            };
        }

        private static Axis CreateAxisX(string[] labels) => AxisX(labels);
        private static Axis CreateAxisY(double? min = 0, double? max = null) => AxisY(min, max);

        // =========================
        // Snapshot (heavy work) – tło
        // =========================
        private sealed class ReportsSnapshot
        {
            public List<ReportsService.ReportItem> CurRows { get; init; } = new();
            public List<ReportsService.ReportItem> PrevRows { get; init; } = new();

            public decimal CurExp { get; init; }
            public decimal CurInc { get; init; }
            public decimal PrevExp { get; init; }
            public decimal PrevInc { get; init; }

            public List<TxLine> TopExp { get; init; } = new();
            public List<TxLine> TopInc { get; init; } = new();

            public int CntExp { get; init; }
            public int CntInc { get; init; }
            public int CntTrf { get; init; }

            public double SumExp { get; init; }
            public double SumInc { get; init; }
            public double SumTrf { get; init; }

            public List<BudgetRow> Budgets { get; init; } = new();
            public string[] BudgetLabels { get; init; } = Array.Empty<string>();
            public double[] BudgetSpent { get; init; } = Array.Empty<double>();
            public double[] BudgetLimit { get; init; } = Array.Empty<double>();
            public double[] BudgetUsagePercent { get; init; } = Array.Empty<double>();

            public List<LoanRow> Loans { get; init; } = new();
            public List<GoalRow> Goals { get; init; } = new();

            public List<PlannedRow> Planned { get; init; } = new();
            public decimal SimDelta { get; init; }
            public string[] SimLabels { get; init; } = Array.Empty<string>();
            public double[] SimValues { get; init; } = Array.Empty<double>();
        }

        private static ReportsSnapshot BuildSnapshot(int uid, DateTime from, DateTime to, DateTime pFrom, DateTime pTo)
        {
            from = from.Date;
            to = to.Date;
            pFrom = pFrom.Date;
            pTo = pTo.Date;

            List<ReportsService.ReportItem> cur;
            List<ReportsService.ReportItem> prev;

            try { cur = ReportsService.LoadReport(uid, "Wszystkie kategorie", "Wszystko", from, to) ?? new(); }
            catch { cur = new(); }

            try { prev = ReportsService.LoadReport(uid, "Wszystkie kategorie", "Wszystko", pFrom, pTo) ?? new(); }
            catch { prev = new(); }

            var curExp = cur.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var curInc = cur.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            var prevExp = prev.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var prevInc = prev.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            var topExp = cur.Where(x => x.Type == "Wydatek")
                .OrderByDescending(x => Math.Abs(x.Amount))
                .Take(3)
                .Select(r => new TxLine { Date = r.Date, Category = r.Category, Amount = Math.Abs(r.Amount) })
                .ToList();

            var topInc = cur.Where(x => x.Type == "Przychód")
                .OrderByDescending(x => Math.Abs(x.Amount))
                .Take(3)
                .Select(r => new TxLine { Date = r.Date, Category = r.Category, Amount = Math.Abs(r.Amount) })
                .ToList();

            var cntExp = cur.Count(x => x.Type == "Wydatek");
            var cntInc = cur.Count(x => x.Type == "Przychód");
            var cntTrf = cur.Count(x => x.Type == "Transfer");

            var sumExp = cur.Where(x => x.Type == "Wydatek").Sum(x => (double)Math.Abs(x.Amount));
            var sumInc = cur.Where(x => x.Type == "Przychód").Sum(x => (double)Math.Abs(x.Amount));
            var sumTrf = cur.Where(x => x.Type == "Transfer").Sum(x => (double)Math.Abs(x.Amount));

            // Budżety
            var budgetsRows = new List<BudgetRow>();
            string[] budgetLabels = new[] { "" };
            double[] budgetSpent = new[] { 0d };
            double[] budgetLimit = new[] { 0d };
            double[] budgetUsage = new[] { 0d };

            try
            {
                var spentByCategory = cur.Where(r => r.Type == "Wydatek")
                    .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                    .ToDictionary(g => g.Key, g => g.Sum(x => Math.Abs(x.Amount)));

                var rawBudgets = BudgetService.GetBudgetsWithSummary(uid) ?? new List<BudgetService.BudgetSummary>();

                foreach (var b in rawBudgets)
                {
                    var plannedAmount = b.PlannedAmount;
                    var spentAmount = b.Spent;

                    // jeśli nazwa budżetu pokrywa się z kategorią -> licz wydatki z okresu
                    if (!string.IsNullOrWhiteSpace(b.Name) && spentByCategory.TryGetValue(b.Name, out var inPeriod))
                        spentAmount = inPeriod;

                    budgetsRows.Add(new BudgetRow
                    {
                        Id = b.Id,
                        Name = b.Name ?? "(budżet)",
                        Planned = plannedAmount,
                        Spent = spentAmount
                    });
                }

                var topBudgets = budgetsRows.OrderByDescending(b => b.Planned).Take(8).ToList();
                budgetLabels = topBudgets.Select(x => x.Name).ToArray();
                budgetSpent = topBudgets.Select(x => (double)x.Spent).ToArray();
                budgetLimit = topBudgets.Select(x => (double)x.Planned).ToArray();

                budgetUsage = topBudgets
                    .Select(x => x.Planned <= 0 ? 0d : (double)(x.Spent / x.Planned * 100m))
                    .ToArray();

                if (budgetLabels.Length == 0) budgetLabels = new[] { "" };
                if (budgetSpent.Length == 0) budgetSpent = new[] { 0d };
                if (budgetLimit.Length == 0) budgetLimit = new[] { 0d };
                if (budgetUsage.Length == 0) budgetUsage = new[] { 0d };
            }
            catch
            {
                // budżety zostają na placeholderach
            }

            // Kredyty – minimal, żeby nie wywalać się na zależnościach
            var loans = new List<LoanRow>();
            try
            {
                var rawLoans = DatabaseService.GetLoans(uid) ?? new List<LoanModel>();
                foreach (var l in rawLoans)
                {
                    loans.Add(new LoanRow
                    {
                        Id = l.Id,
                        Name = l.Name ?? "Kredyt",
                        RemainingToPay = 0m,
                        PaidInPeriod = 0m,
                        OverpaidInPeriod = 0m
                    });
                }
            }
            catch { }

            // Cele
            var goals = new List<GoalRow>();
            try
            {
                var envGoals = DatabaseService.GetEnvelopeGoals(uid);
                if (envGoals != null)
                {
                    var periodLenDays = Math.Max(1, (to - from).Days + 1);
                    foreach (var g in envGoals)
                    {
                        var row = new GoalRow
                        {
                            Name = g.Name ?? "Cel",
                            Target = g.Target,
                            Current = g.Allocated,
                            DueDate = g.Deadline
                        };
                        row.NeededNextPeriod = CalculateNeededForNextPeriod(row, periodLenDays);
                        goals.Add(row);
                    }
                }
            }
            catch { }

            // Symulacja – placeholder
            var plannedTx = new List<PlannedRow>();
            decimal simDelta = 0m;
            string[] simLabels = new[] { "" };
            double[] simValues = new[] { 0d };

            try
            {
                simDelta = plannedTx.Sum(x => x.Amount);
            }
            catch { simDelta = 0m; }

            return new ReportsSnapshot
            {
                CurRows = cur,
                PrevRows = prev,

                CurExp = curExp,
                CurInc = curInc,
                PrevExp = prevExp,
                PrevInc = prevInc,

                TopExp = topExp,
                TopInc = topInc,

                CntExp = cntExp,
                CntInc = cntInc,
                CntTrf = cntTrf,

                SumExp = sumExp,
                SumInc = sumInc,
                SumTrf = sumTrf,

                Budgets = budgetsRows,
                BudgetLabels = budgetLabels,
                BudgetSpent = budgetSpent,
                BudgetLimit = budgetLimit,
                BudgetUsagePercent = budgetUsage,

                Loans = loans,
                Goals = goals,

                Planned = plannedTx,
                SimDelta = simDelta,
                SimLabels = simLabels,
                SimValues = simValues
            };
        }

        // =========================
        // Apply snapshot – UI thread
        // =========================
        private void ApplySnapshot(ReportsSnapshot s)
        {
            s ??= new ReportsSnapshot();

            // 1) Rows
            Rows = new ObservableCollection<ReportsService.ReportItem>(s.CurRows ?? new List<ReportsService.ReportItem>());
            PreviousRows = new ObservableCollection<ReportsService.ReportItem>(s.PrevRows ?? new List<ReportsService.ReportItem>());
            RebuildTransactionsView();

            // 2) KPI
            TotalExpenses = s.CurExp;
            TotalIncomes = s.CurInc;
            Balance = TotalIncomes - TotalExpenses;

            PreviousTotalExpenses = s.PrevExp;
            PreviousTotalIncomes = s.PrevInc;
            PreviousBalance = PreviousTotalIncomes - PreviousTotalExpenses;

            DeltaExpenses = TotalExpenses - PreviousTotalExpenses;
            DeltaIncomes = TotalIncomes - PreviousTotalIncomes;
            DeltaBalance = Balance - PreviousBalance;

            CompareExpensesText = (DeltaExpenses == 0m)
                ? "bez zmian względem poprzedniego okresu."
                : (DeltaExpenses > 0m
                    ? $"wydajesz o {Money(DeltaExpenses)} więcej niż w poprzednim okresie."
                    : $"wydajesz o {Money(Math.Abs(DeltaExpenses))} mniej niż w poprzednim okresie.");

            CompareIncomesText = (DeltaIncomes == 0m)
                ? "bez zmian względem poprzedniego okresu."
                : (DeltaIncomes > 0m
                    ? $"Twoje wpływy były większe o {Money(DeltaIncomes)} niż w poprzednim okresie."
                    : $"Twoje wpływy były mniejsze o {Money(Math.Abs(DeltaIncomes))} niż w poprzednim okresie.");

            CompareBalanceText = (DeltaBalance == 0m)
                ? "bez zmian względem poprzedniego okresu."
                : (DeltaBalance > 0m
                    ? $"jesteś na plusie o {Money(DeltaBalance)} względem poprzedniego okresu."
                    : $"jesteś na minusie o {Money(Math.Abs(DeltaBalance))} względem poprzedniego okresu.");

            // 3) Podium
            OverviewTopExpenses.Clear();
            foreach (var x in s.TopExp ?? new List<TxLine>())
                OverviewTopExpenses.Add(x);

            OverviewTopIncomes.Clear();
            foreach (var x in s.TopInc ?? new List<TxLine>())
                OverviewTopIncomes.Add(x);

            var expList = s.TopExp ?? new List<TxLine>();
            var incList = s.TopInc ?? new List<TxLine>();

            TopExp1 = GetAt(expList, 0);
            TopExp2 = GetAt(expList, 1);
            TopExp3 = GetAt(expList, 2);

            TopInc1 = GetAt(incList, 0);
            TopInc2 = GetAt(incList, 1);
            TopInc3 = GetAt(incList, 2);

            // 4) Wykresy przeglądu
            var xLabels = new[] { "Wydatki", "Przychody", "Transfery" };

            OverviewCountAxesX = new[] { CreateAxisX(xLabels) };
            OverviewCountAxesY = new[] { CreateAxisY(min: 0) };

            OverviewAmountAxesX = new[] { CreateAxisX(xLabels) };
            OverviewAmountAxesY = new[] { CreateAxisY(min: 0) };

            // 3 osobne serie z wartościami w odpowiednich slotach (czytelne i stabilne)
            OverviewCountSeries = new ISeries[]
            {
                new StackedColumnSeries<double?>
                {
                    Name = "Wydatki",
                    Values = new double?[] { s.CntExp, null, null },
                    Fill = new SolidColorPaint(new SKColor(220, 60, 60))
                },
                new StackedColumnSeries<double?>
                {
                    Name = "Przychody",
                    Values = new double?[] { null, s.CntInc, null },
                    Fill = new SolidColorPaint(new SKColor(60, 200, 120))
                },
                new StackedColumnSeries<double?>
                {
                    Name = "Transfery",
                    Values = new double?[] { null, null, s.CntTrf },
                    Fill = new SolidColorPaint(new SKColor(160, 90, 255))
                }
            };

            OverviewAmountSeries = new ISeries[]
            {
                new StackedColumnSeries<double?>
                {
                    Name = "Wydatki",
                    Values = new double?[] { s.SumExp, null, null },
                    Fill = new SolidColorPaint(new SKColor(220, 60, 60))
                },
                new StackedColumnSeries<double?>
                {
                    Name = "Przychody",
                    Values = new double?[] { null, s.SumInc, null },
                    Fill = new SolidColorPaint(new SKColor(60, 200, 120))
                },
                new StackedColumnSeries<double?>
                {
                    Name = "Transfery",
                    Values = new double?[] { null, null, s.SumTrf },
                    Fill = new SolidColorPaint(new SKColor(160, 90, 255))
                }
            };

            // 5) Budżety (tabela + KPI)
            Budgets.Clear();
            foreach (var b in s.Budgets ?? new List<BudgetRow>())
                Budgets.Add(b);

            var budgetsList = s.Budgets ?? new List<BudgetRow>();
            BudgetsOkCount = budgetsList.Count(b => !b.IsOver);
            BudgetsOverCount = budgetsList.Count(b => b.IsOver);
            BudgetsOverTotal = budgetsList.Where(b => b.IsOver).Sum(b => b.Delta);

            // 6) Budżety: wykres Wydano vs Limit (TOP8)
            var bx = (s.BudgetLabels != null && s.BudgetLabels.Length > 0) ? s.BudgetLabels : new[] { "" };
            var bs = (s.BudgetSpent != null && s.BudgetSpent.Length > 0) ? s.BudgetSpent : new[] { 0d };
            var bl = (s.BudgetLimit != null && s.BudgetLimit.Length > 0) ? s.BudgetLimit : new[] { 0d };

            BudgetsAxesX = new[] { CreateAxisX(bx) };
            BudgetsAxesY = new[] { CreateAxisY(min: 0) };
            BudgetsSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Wydano", Values = bs },
                new ColumnSeries<double> { Name = "Limit",  Values = bl }
            };

            // 7) Budżety: wykres użycia %
            var bu = (s.BudgetUsagePercent != null && s.BudgetUsagePercent.Length > 0) ? s.BudgetUsagePercent : new[] { 0d };
            BudgetsUsageAxesX = new[] { CreateAxisX(bx) };
            BudgetsUsageAxesY = new[] { CreateAxisY(min: 0, max: 160) };
            BudgetsUsageSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Użycie (%)", Values = bu }
            };

            // 8) Kredyty
            Loans.Clear();
            foreach (var l in s.Loans ?? new List<LoanRow>())
                Loans.Add(l);

            // 9) Cele
            Goals.Clear();
            foreach (var g in s.Goals ?? new List<GoalRow>())
                Goals.Add(g);

            // 10) Symulacja
            PlannedSim.Clear();
            foreach (var p in (s.Planned ?? new List<PlannedRow>()).OrderBy(x => x.Date))
                PlannedSim.Add(p);

            SimBalanceDelta = s.SimDelta;

            var simX = (s.SimLabels != null && s.SimLabels.Length > 0) ? s.SimLabels : new[] { "" };
            var simY = (s.SimValues != null && s.SimValues.Length > 0) ? s.SimValues : new[] { 0d };

            SimulationAxesX = new[] { CreateAxisX(simX) };
            SimulationAxesY = new[] { CreateAxisY(min: null) };
            SimulationSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "Saldo (symulacja)",
                    Values = simY,
                    GeometrySize = 0
                }
            };

            // 11) Donuty (placeholder)
            ExpenseDonutSeries = Array.Empty<ISeries>();
            IncomeDonutSeries = Array.Empty<ISeries>();
            TransferDonutSeries = Array.Empty<ISeries>();
        }

        // =========================
        // Helpers
        // =========================
        private static decimal CalculateNeededForNextPeriod(GoalRow row, int periodLenDays)
        {
            if (row.Missing <= 0) return 0m;
            if (row.DueDate == null) return row.Missing;

            var today = DateTime.Today;
            var due = row.DueDate.Value.Date;
            if (due <= today) return row.Missing;

            var daysLeft = (due - today).Days;
            var periodsLeft = (int)Math.Ceiling(daysLeft / (double)Math.Max(1, periodLenDays));
            periodsLeft = Math.Max(1, periodsLeft);

            return Math.Max(0m, row.Missing / periodsLeft);
        }

        private static string[] SafeLabels(IEnumerable<string>? labels)
        {
            var arr = labels?.ToArray() ?? Array.Empty<string>();
            return arr.Length == 0 ? new[] { "" } : arr;
        }

        private static double[] SafeValues(IEnumerable<double>? values)
        {
            var arr = values?.ToArray() ?? Array.Empty<double>();
            return arr.Length == 0 ? new[] { 0d } : arr;
        }

        private static Axis AxisX(string[] labels)
        {
            return new Axis
            {
                Labels = labels,
                SeparatorsAtCenter = true,
                LabelsPaint = new SolidColorPaint(SKColors.White),
                NamePaint = new SolidColorPaint(SKColors.White),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 35)),
                TicksPaint = new SolidColorPaint(new SKColor(255, 255, 255, 60)),
                TextSize = 12
            };
        }

        private static Axis AxisY(double? min = 0, double? max = null)
        {
            return new Axis
            {
                MinLimit = min,
                MaxLimit = max,
                LabelsPaint = new SolidColorPaint(SKColors.White),
                NamePaint = new SolidColorPaint(SKColors.White),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 35)),
                TicksPaint = new SolidColorPaint(new SKColor(255, 255, 255, 60)),
                TextSize = 12
            };
        }

        // =========================
        // Export PDF
        // =========================
        private void ExportPdf(string? tabKey)
        {
            try
            {
                var path = PdfExportService.ExportReportsPdf(this, null);
                ToastService.Success($"Raport PDF zapisano na pulpicie: {path}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"Błąd eksportu PDF: {ex.Message}");
            }
        }
    }
}
