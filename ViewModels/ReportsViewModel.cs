using Finly.Helpers;
using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;

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

            // Bazowe dane
            Rows = new ObservableCollection<ReportsService.ReportItem>();
            PreviousRows = new ObservableCollection<ReportsService.ReportItem>();

            // Przegląd: podium
            OverviewTopExpenses = new ObservableCollection<TxLine>();
            OverviewTopIncomes = new ObservableCollection<TxLine>();

            // Budżety / Kredyty / Cele / Inwestycje / Symulacja
            Budgets = new ObservableCollection<BudgetRow>();
            Loans = new ObservableCollection<LoanRow>();
            Goals = new ObservableCollection<GoalRow>();
            Investments = new ObservableCollection<InvestmentRow>();
            PlannedSim = new ObservableCollection<PlannedRow>();

            // Komendy
            RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());


            // Tab eksport (na razie wspólna metoda eksportu; później łatwo rozdzielimy per zakładka)
            ExportPdfCommand = new RelayCommand(param => ExportPdf(param?.ToString()));

            // Startowe serie/osi, żeby XAML miał co bindować od razu
            InitAllCharts();

        }

        private int _refreshing;
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

        public async System.Threading.Tasks.Task RefreshAsync()
        {
            // blokada re-entrancy
            if (Interlocked.Exchange(ref _refreshing, 1) == 1)
                return;

            try
            {
                var uid = UserService.GetCurrentUserId();
                if (uid <= 0)
                    throw new InvalidOperationException("Brak zalogowanego użytkownika.");

                // snapshot dat (żeby w trakcie Task.Run nie zmieniły się)
                var from = FromDate;
                var to = ToDate;
                var pFrom = PreviousFrom;
                var pTo = PreviousTo;

                // 1) ciężkie rzeczy liczymy w tle
                var snap = await Task.Run(() => BuildSnapshot(uid, from, to, pFrom, pTo));

                // 2) aplikujemy na UI (tu już jesteśmy z powrotem na wątku UI)
                ApplySnapshot(snap);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

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

            public List<LoanRow> Loans { get; init; } = new();
            public List<GoalRow> Goals { get; init; } = new();

            public List<PlannedRow> Planned { get; init; } = new();
            public decimal SimDelta { get; init; }
            public string[] SimLabels { get; init; } = Array.Empty<string>();
            public double[] SimValues { get; init; } = Array.Empty<double>();
        }

        private static ReportsSnapshot BuildSnapshot(int uid, DateTime from, DateTime to, DateTime pFrom, DateTime pTo)
        {
            // Normalizacja dat (na wszelki wypadek)
            from = from.Date;
            to = to.Date;
            pFrom = pFrom.Date;
            pTo = pTo.Date;

            // 1) rows (best-effort, ale bez wywalania całego snapshotu)
            List<ReportsService.ReportItem> cur;
            List<ReportsService.ReportItem> prev;

            try
            {
                cur = ReportsService.LoadReport(uid, "Wszystkie kategorie", "Wszystko", from, to) ?? new List<ReportsService.ReportItem>();
            }
            catch
            {
                cur = new List<ReportsService.ReportItem>();
            }

            try
            {
                prev = ReportsService.LoadReport(uid, "Wszystkie kategorie", "Wszystko", pFrom, pTo) ?? new List<ReportsService.ReportItem>();
            }
            catch
            {
                prev = new List<ReportsService.ReportItem>();
            }

            // 2) KPI (liczymy na listach)
            var curExp = cur.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var curInc = cur.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            var prevExp = prev.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var prevInc = prev.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            // 3) podium
            var topExp = cur.Where(x => x.Type == "Wydatek")
                .OrderByDescending(x => Math.Abs(x.Amount))
                .Take(3)
                .Select(r => new TxLine
                {
                    Date = r.Date,
                    Category = r.Category,
                    Amount = Math.Abs(r.Amount)
                })
                .ToList();

            var topInc = cur.Where(x => x.Type == "Przychód")
                .OrderByDescending(x => Math.Abs(x.Amount))
                .Take(3)
                .Select(r => new TxLine
                {
                    Date = r.Date,
                    Category = r.Category,
                    Amount = Math.Abs(r.Amount)
                })
                .ToList();

            // 4) overview chart numbers
            var cntExp = cur.Count(x => x.Type == "Wydatek");
            var cntInc = cur.Count(x => x.Type == "Przychód");
            var cntTrf = cur.Count(x => x.Type == "Transfer");

            var sumExp = cur.Where(x => x.Type == "Wydatek").Sum(x => (double)Math.Abs(x.Amount));
            var sumInc = cur.Where(x => x.Type == "Przychód").Sum(x => (double)Math.Abs(x.Amount));
            var sumTrf = cur.Where(x => x.Type == "Transfer").Sum(x => (double)Math.Abs(x.Amount));

            // 5) budżety
            var budgetsRows = new List<BudgetRow>();
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

                    // Jeżeli da się, podmieniamy spent na realnie wydane w okresie po kategorii (nazwa budżetu = kategoria)
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
            }
            catch
            {
                // celowo cisza (raport ma się wygenerować nawet jak budżety się wysypią)
            }

            var topBudgets = budgetsRows
                .OrderByDescending(b => b.Planned)
                .Take(8)
                .ToList();

            var budgetLabels = topBudgets.Select(x => x.Name).ToArray();
            var budgetSpent = topBudgets.Select(x => (double)x.Spent).ToArray();
            var budgetLimit = topBudgets.Select(x => (double)x.Planned).ToArray();

            if (budgetLabels.Length == 0) budgetLabels = new[] { "" };
            if (budgetSpent.Length == 0) budgetSpent = new[] { 0d };
            if (budgetLimit.Length == 0) budgetLimit = new[] { 0d };

            // 6) loans
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

            // 6b) goals
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

            // 7) planned sim – placeholder (żeby kompilacja i UI działały)
            var plannedTx = new List<PlannedRow>();
            decimal simDelta = 0m;

            // placeholdery wykresu symulacji – zawsze niepuste
            string[] simLabels = new[] { "" };
            double[] simValues = new[] { 0d };

            try
            {
                // Jeśli później podepniesz realne query:
                // - uzupełnij plannedTx
                // - simDelta = plannedTx.Sum(x => x.Amount)
                // - simLabels/simValues wylicz jak w BuildSimulationChart (ale bez UI)

                simDelta = plannedTx.Sum(x => x.Amount);
            }
            catch
            {
                simDelta = 0m;
            }

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

                Loans = loans,
                Goals = goals,

                Planned = plannedTx,
                SimDelta = simDelta,
                SimLabels = simLabels,
                SimValues = simValues
            };
        }


        private void ApplySnapshot(ReportsSnapshot s)
        {
            // Rows
            Rows = new ObservableCollection<ReportsService.ReportItem>(s.CurRows);
            PreviousRows = new ObservableCollection<ReportsService.ReportItem>(s.PrevRows);

            // KPI
            TotalExpenses = s.CurExp;
            TotalIncomes = s.CurInc;
            Balance = TotalIncomes - TotalExpenses;

            PreviousTotalExpenses = s.PrevExp;
            PreviousTotalIncomes = s.PrevInc;
            PreviousBalance = PreviousTotalIncomes - PreviousTotalExpenses;

            DeltaExpenses = TotalExpenses - PreviousTotalExpenses;
            DeltaIncomes = TotalIncomes - PreviousTotalIncomes;
            DeltaBalance = Balance - PreviousBalance;

            // Podium
            OverviewTopExpenses.Clear();
            foreach (var x in s.TopExp) OverviewTopExpenses.Add(x);

            OverviewTopIncomes.Clear();
            foreach (var x in s.TopInc) OverviewTopIncomes.Add(x);

            // Overview charts
            OverviewCountSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name="Ilość", Values = new double[] { s.CntExp, s.CntInc, s.CntTrf } }
            };

            OverviewAmountSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name="Wartość (zł)", Values = new double[] { s.SumExp, s.SumInc, s.SumTrf } }
            };

            // Budgets
            Budgets.Clear();
            foreach (var b in s.Budgets) Budgets.Add(b);

            BudgetsAxesX = new[] { new Axis { Labels = s.BudgetLabels } };
            BudgetsAxesY = new[] { new Axis { MinLimit = 0 } };
            BudgetsSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name="Wydano", Values = s.BudgetSpent },
        new ColumnSeries<double> { Name="Limit",  Values = s.BudgetLimit }
            };

            // Loans/Goals
            Loans.Clear();
            foreach (var l in s.Loans) Loans.Add(l);

            Goals.Clear();
            foreach (var g in s.Goals) Goals.Add(g);

            // Planned
            PlannedSim.Clear();
            foreach (var p in s.Planned.OrderBy(x => x.Date)) PlannedSim.Add(p);

            SimBalanceDelta = s.SimDelta;

            // Sym chart (jeśli dopniesz dane)
            SimulationAxesX = new[] { new Axis { Labels = s.SimLabels } };
            SimulationSeries = new ISeries[] { new LineSeries<double> { Name = "Saldo (symulacja)", Values = s.SimValues } };

            // Donuty budujesz po ApplySnapshot (możesz też tu):
            BuildCategoryDonuts();
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
        // KPI – obecny / poprzedni / delta
        // =========================
        private decimal _totalExpenses;
        public decimal TotalExpenses
        {
            get => _totalExpenses;
            private set { if (_totalExpenses != value) { _totalExpenses = value; Raise(nameof(TotalExpenses)); Raise(nameof(TotalExpensesStr)); } }
        }
        public string TotalExpensesStr => Money(TotalExpenses);

        private decimal _totalIncomes;
        public decimal TotalIncomes
        {
            get => _totalIncomes;
            private set { if (_totalIncomes != value) { _totalIncomes = value; Raise(nameof(TotalIncomes)); Raise(nameof(TotalIncomesStr)); } }
        }
        public string TotalIncomesStr => Money(TotalIncomes);

        private decimal _balance;
        public decimal Balance
        {
            get => _balance;
            private set { if (_balance != value) { _balance = value; Raise(nameof(Balance)); Raise(nameof(BalanceStr)); } }
        }
        public string BalanceStr => Money(Balance);

        private decimal _previousTotalExpenses;
        public decimal PreviousTotalExpenses
        {
            get => _previousTotalExpenses;
            private set { if (_previousTotalExpenses != value) { _previousTotalExpenses = value; Raise(nameof(PreviousTotalExpenses)); Raise(nameof(PreviousTotalExpensesStr)); } }
        }
        public string PreviousTotalExpensesStr => Money(PreviousTotalExpenses);

        private decimal _previousTotalIncomes;
        public decimal PreviousTotalIncomes
        {
            get => _previousTotalIncomes;
            private set { if (_previousTotalIncomes != value) { _previousTotalIncomes = value; Raise(nameof(PreviousTotalIncomes)); Raise(nameof(PreviousTotalIncomesStr)); } }
        }
        public string PreviousTotalIncomesStr => Money(PreviousTotalIncomes);

        private decimal _previousBalance;
        public decimal PreviousBalance
        {
            get => _previousBalance;
            private set { if (_previousBalance != value) { _previousBalance = value; Raise(nameof(PreviousBalance)); Raise(nameof(PreviousBalanceStr)); } }
        }
        public string PreviousBalanceStr => Money(PreviousBalance);

        private decimal _deltaExpenses;
        public decimal DeltaExpenses
        {
            get => _deltaExpenses;
            private set { if (_deltaExpenses != value) { _deltaExpenses = value; Raise(nameof(DeltaExpenses)); Raise(nameof(DeltaExpensesStr)); } }
        }
        public string DeltaExpensesStr => DeltaMoney(DeltaExpenses);

        private decimal _deltaIncomes;
        public decimal DeltaIncomes
        {
            get => _deltaIncomes;
            private set { if (_deltaIncomes != value) { _deltaIncomes = value; Raise(nameof(DeltaIncomes)); Raise(nameof(DeltaIncomesStr)); } }
        }
        public string DeltaIncomesStr => DeltaMoney(DeltaIncomes);

        private decimal _deltaBalance;
        public decimal DeltaBalance
        {
            get => _deltaBalance;
            private set { if (_deltaBalance != value) { _deltaBalance = value; Raise(nameof(DeltaBalance)); Raise(nameof(DeltaBalanceStr)); } }
        }
        public string DeltaBalanceStr => DeltaMoney(DeltaBalance);

        private static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        private static string DeltaMoney(decimal v)
        {
            var sign = v > 0 ? "+" : v < 0 ? "-" : "";
            return $"{sign}{Math.Abs(v).ToString("N2", CultureInfo.CurrentCulture)} zł";
        }

        // =========================
        // PRZEGLĄD: podium TOP3
        // =========================
        public sealed class TxLine
        {
            public DateTime Date { get; set; }
            public string Category { get; set; } = "";
            public decimal Amount { get; set; }
            public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public ObservableCollection<TxLine> OverviewTopExpenses { get; }
        public ObservableCollection<TxLine> OverviewTopIncomes { get; }

        // =========================
        // BUDŻETY / KREDYTY / CELE / INWESTYCJE / SYMULACJA – modele tabel
        // =========================
        public sealed class BudgetRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Planned { get; set; }
            public decimal Spent { get; set; }

            public bool IsOver => Spent > Planned;
            public decimal Delta => Spent - Planned;              // >0 przekroczono, <0 zapas
            public decimal Remaining => Planned - Spent;          // >0 zapas, <0 przekroczono
            public decimal UsedPercent => Planned <= 0 ? 0 : (Spent / Planned * 100m);

            public string PlannedStr => Money(Planned);
            public string SpentStr => Money(Spent);
            public string DeltaStr => DeltaMoney(Delta);
            public string RemainingStr => Money(Remaining);
            public string UsedPercentStr => UsedPercent.ToString("N1", CultureInfo.CurrentCulture) + " %";
        }

        public ObservableCollection<BudgetRow> Budgets { get; }

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

            public decimal NeededNextPeriod { get; set; } // rekomendacja na kolejny okres

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
            private set { if (_simBalanceDelta != value) { _simBalanceDelta = value; Raise(nameof(SimBalanceDelta)); Raise(nameof(SimBalanceDeltaStr)); } }
        }
        public string SimBalanceDeltaStr => Money(SimBalanceDelta);

        // =========================
        // LIVECHARTS – BINDINGI POD XAML
        // =========================

        // ---- 1) Przegląd: wykres ilości transakcji
        private ISeries[] _overviewCountSeries = Array.Empty<ISeries>();
        public ISeries[] OverviewCountSeries { get => _overviewCountSeries; private set { _overviewCountSeries = value; Raise(nameof(OverviewCountSeries)); } }

        private Axis[] _overviewCountAxesX = Array.Empty<Axis>();
        public Axis[] OverviewCountAxesX { get => _overviewCountAxesX; private set { _overviewCountAxesX = value; Raise(nameof(OverviewCountAxesX)); } }

        private Axis[] _overviewCountAxesY = Array.Empty<Axis>();
        public Axis[] OverviewCountAxesY { get => _overviewCountAxesY; private set { _overviewCountAxesY = value; Raise(nameof(OverviewCountAxesY)); } }

        // ---- 1) Przegląd: wykres wartości (zł)
        private ISeries[] _overviewAmountSeries = Array.Empty<ISeries>();
        public ISeries[] OverviewAmountSeries { get => _overviewAmountSeries; private set { _overviewAmountSeries = value; Raise(nameof(OverviewAmountSeries)); } }

        private Axis[] _overviewAmountAxesX = Array.Empty<Axis>();
        public Axis[] OverviewAmountAxesX { get => _overviewAmountAxesX; private set { _overviewAmountAxesX = value; Raise(nameof(OverviewAmountAxesX)); } }

        private Axis[] _overviewAmountAxesY = Array.Empty<Axis>();
        public Axis[] OverviewAmountAxesY { get => _overviewAmountAxesY; private set { _overviewAmountAxesY = value; Raise(nameof(OverviewAmountAxesY)); } }

        // ---- 2) Budżety
        private ISeries[] _budgetsSeries = Array.Empty<ISeries>();
        public ISeries[] BudgetsSeries { get => _budgetsSeries; private set { _budgetsSeries = value; Raise(nameof(BudgetsSeries)); } }

        private Axis[] _budgetsAxesX = Array.Empty<Axis>();
        public Axis[] BudgetsAxesX { get => _budgetsAxesX; private set { _budgetsAxesX = value; Raise(nameof(BudgetsAxesX)); } }

        private Axis[] _budgetsAxesY = Array.Empty<Axis>();
        public Axis[] BudgetsAxesY { get => _budgetsAxesY; private set { _budgetsAxesY = value; Raise(nameof(BudgetsAxesY)); } }

        // ---- 3) Inwestycje
        private ISeries[] _investmentsSeries = Array.Empty<ISeries>();
        public ISeries[] InvestmentsSeries { get => _investmentsSeries; private set { _investmentsSeries = value; Raise(nameof(InvestmentsSeries)); } }

        private Axis[] _investmentsAxesX = Array.Empty<Axis>();
        public Axis[] InvestmentsAxesX { get => _investmentsAxesX; private set { _investmentsAxesX = value; Raise(nameof(InvestmentsAxesX)); } }

        private Axis[] _investmentsAxesY = Array.Empty<Axis>();
        public Axis[] InvestmentsAxesY { get => _investmentsAxesY; private set { _investmentsAxesY = value; Raise(nameof(InvestmentsAxesY)); } }

        // ---- 4) Kategorie (donuty)
        private ISeries[] _expenseDonutSeries = Array.Empty<ISeries>();
        public ISeries[] ExpenseDonutSeries { get => _expenseDonutSeries; private set { _expenseDonutSeries = value; Raise(nameof(ExpenseDonutSeries)); } }

        private ISeries[] _incomeDonutSeries = Array.Empty<ISeries>();
        public ISeries[] IncomeDonutSeries { get => _incomeDonutSeries; private set { _incomeDonutSeries = value; Raise(nameof(IncomeDonutSeries)); } }

        private ISeries[] _transferDonutSeries = Array.Empty<ISeries>();
        public ISeries[] TransferDonutSeries { get => _transferDonutSeries; private set { _transferDonutSeries = value; Raise(nameof(TransferDonutSeries)); } }

        // ---- 5) Kredyty
        private ISeries[] _loansSeries = Array.Empty<ISeries>();
        public ISeries[] LoansSeries { get => _loansSeries; private set { _loansSeries = value; Raise(nameof(LoansSeries)); } }

        private Axis[] _loansAxesX = Array.Empty<Axis>();
        public Axis[] LoansAxesX { get => _loansAxesX; private set { _loansAxesX = value; Raise(nameof(LoansAxesX)); } }

        private Axis[] _loansAxesY = Array.Empty<Axis>();
        public Axis[] LoansAxesY { get => _loansAxesY; private set { _loansAxesY = value; Raise(nameof(LoansAxesY)); } }

        // ---- 6) Cele
        private ISeries[] _goalsSeries = Array.Empty<ISeries>();
        public ISeries[] GoalsSeries { get => _goalsSeries; private set { _goalsSeries = value; Raise(nameof(GoalsSeries)); } }

        private Axis[] _goalsAxesX = Array.Empty<Axis>();
        public Axis[] GoalsAxesX { get => _goalsAxesX; private set { _goalsAxesX = value; Raise(nameof(GoalsAxesX)); } }

        private Axis[] _goalsAxesY = Array.Empty<Axis>();
        public Axis[] GoalsAxesY { get => _goalsAxesY; private set { _goalsAxesY = value; Raise(nameof(GoalsAxesY)); } }

        // ---- 7) Symulacja
        private ISeries[] _simulationSeries = Array.Empty<ISeries>();
        public ISeries[] SimulationSeries { get => _simulationSeries; private set { _simulationSeries = value; Raise(nameof(SimulationSeries)); } }

        private Axis[] _simulationAxesX = Array.Empty<Axis>();
        public Axis[] SimulationAxesX { get => _simulationAxesX; private set { _simulationAxesX = value; Raise(nameof(SimulationAxesX)); } }

        private Axis[] _simulationAxesY = Array.Empty<Axis>();
        public Axis[] SimulationAxesY { get => _simulationAxesY; private set { _simulationAxesY = value; Raise(nameof(SimulationAxesY)); } }

        private void InitAllCharts()
        {
            // Wspólne etykiety
            var txLabels = new[] { "Wydatki", "Przychody", "Transfery" };

            // =========================
            // 1) Overview: COUNT
            // =========================
            OverviewCountAxesX = new[] { new Axis { Labels = txLabels } };
            OverviewCountAxesY = new[] { new Axis { MinLimit = 0 } };
            OverviewCountSeries = new ISeries[]
            {
        new ColumnSeries<double>
        {
            Name = "Ilość",
            Values = new double[] { 0, 0, 0 }
        }
            };

            // =========================
            // 1) Overview: AMOUNT
            // =========================
            OverviewAmountAxesX = new[] { new Axis { Labels = txLabels } };
            OverviewAmountAxesY = new[] { new Axis { MinLimit = 0 } };
            OverviewAmountSeries = new ISeries[]
            {
        new ColumnSeries<double>
        {
            Name = "Wartość (zł)",
            Values = new double[] { 0, 0, 0 }
        }
            };

            // =========================
            // 2) Budżety (placeholder)
            // =========================
            BudgetsAxesX = new[] { new Axis { Labels = SafeLabels(null) } };
            BudgetsAxesY = new[] { new Axis { MinLimit = 0 } };
            BudgetsSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Wydano", Values = SafeValues(null) },
        new ColumnSeries<double> { Name = "Limit",  Values = SafeValues(null) }
            };

            // =========================
            // 3) Inwestycje (placeholder)  <-- KLUCZ: bez pustych Labels/Values
            // =========================
            InvestmentsAxesX = new[] { new Axis { Labels = SafeLabels(null) } };
            InvestmentsAxesY = new[] { new Axis { MinLimit = 0 } };
            InvestmentsSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Zysk/Strata", Values = SafeValues(null) }
            };

            // =========================
            // 4) Kategorie (donuty) – placeholder
            // (tu może być pusto, bo to nie jest CartesianChart – ale zostawiamy stabilnie)
            // =========================
            ExpenseDonutSeries = Array.Empty<ISeries>();
            IncomeDonutSeries = Array.Empty<ISeries>();
            TransferDonutSeries = Array.Empty<ISeries>();

            // =========================
            // 5) Kredyty (placeholder)  <-- KLUCZ: bez pustych Labels/Values
            // =========================
            LoansAxesX = new[] { new Axis { Labels = SafeLabels(null) } };
            LoansAxesY = new[] { new Axis { MinLimit = 0 } };
            LoansSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Spłacono",  Values = SafeValues(null) },
        new ColumnSeries<double> { Name = "Nadpłaty",  Values = SafeValues(null) },
        new ColumnSeries<double> { Name = "Pozostało", Values = SafeValues(null) }
            };

            // =========================
            // 6) Cele (placeholder)  <-- KLUCZ: bez pustych Labels/Values
            // =========================
            GoalsAxesX = new[] { new Axis { Labels = SafeLabels(null) } };
            GoalsAxesY = new[] { new Axis { MinLimit = 0, MaxLimit = 100 } };
            GoalsSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Postęp (%)", Values = SafeValues(null) }
            };

            // =========================
            // 7) Symulacja (placeholder)  <-- KLUCZ: bez pustych Labels/Values
            // =========================
            SimulationAxesX = new[] { new Axis { Labels = SafeLabels(null) } };
            SimulationAxesY = new[] { new Axis() };
            SimulationSeries = new ISeries[]
            {
        new LineSeries<double> { Name = "Saldo (symulacja)", Values = SafeValues(null) }
            };
        }


        // =========================
        // Odświeżenie główne
        // =========================
        private void Refresh()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                if (uid <= 0)
                {
                    MessageBox.Show("Brak zalogowanego użytkownika.", "Błąd",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 1) Obecny okres
                var cur = LoadRows(uid, FromDate, ToDate);
                Rows = new ObservableCollection<ReportsService.ReportItem>(cur);

                // 2) Poprzedni okres
                var prev = LoadRows(uid, PreviousFrom, PreviousTo);
                PreviousRows = new ObservableCollection<ReportsService.ReportItem>(prev);


                // 3) KPI
                RecalcTotalsForBoth();

                // 4) Przegląd: podium + wykresy
                BuildOverview();

                // 5) Budżety / Kredyty / Cele / Inwestycje / Symulacja
                BuildBudgets(uid);
                BuildInvestments(uid);
                BuildLoans(uid);
                BuildGoals(uid);
                BuildPlannedSimulation(uid);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd odświeżania raportów: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static List<ReportsService.ReportItem> LoadRows(int uid, DateTime from, DateTime to)
        {
            return ReportsService.LoadReport(
                userId: uid,
                category: "Wszystkie kategorie",
                transactionType: "Wszystko",
                from: from,
                to: to
            );
        }


        private void RecalcTotalsForBoth()
        {
            // OBECNY
            var curExp = Rows.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var curInc = Rows.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            TotalExpenses = curExp;
            TotalIncomes = curInc;
            Balance = TotalIncomes - TotalExpenses;

            // POPRZEDNI
            var prevExp = PreviousRows.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var prevInc = PreviousRows.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            PreviousTotalExpenses = prevExp;
            PreviousTotalIncomes = prevInc;
            PreviousBalance = PreviousTotalIncomes - PreviousTotalExpenses;

            // DELTY
            DeltaExpenses = TotalExpenses - PreviousTotalExpenses;
            DeltaIncomes = TotalIncomes - PreviousTotalIncomes;
            DeltaBalance = Balance - PreviousBalance;

            Raise(nameof(PeriodLabel));
            Raise(nameof(PreviousPeriodLabel));
        }

        // =========================
        // 1) PRZEGLĄD OGÓLNY
        // =========================
        private void BuildOverview()
        {
            // TOP 3 wydatki
            OverviewTopExpenses.Clear();
            foreach (var r in Rows
                         .Where(x => x.Type == "Wydatek")
                         .OrderByDescending(x => Math.Abs(x.Amount))
                         .Take(3))
            {
                OverviewTopExpenses.Add(new TxLine
                {
                    Date = r.Date,
                    Category = r.Category,
                    Amount = Math.Abs(r.Amount)
                });
            }

            // TOP 3 przychody
            OverviewTopIncomes.Clear();
            foreach (var r in Rows
                         .Where(x => x.Type == "Przychód")
                         .OrderByDescending(x => Math.Abs(x.Amount))
                         .Take(3))
            {
                OverviewTopIncomes.Add(new TxLine
                {
                    Date = r.Date,
                    Category = r.Category,
                    Amount = Math.Abs(r.Amount)
                });
            }

            // Wykres ilości (wydatki/przychody/transfery)
            var cntExp = Rows.Count(x => x.Type == "Wydatek");
            var cntInc = Rows.Count(x => x.Type == "Przychód");
            var cntTrf = Rows.Count(x => x.Type == "Transfer"); // na razie 0, dopóki ReportsService nie ładuje transferów

            OverviewCountSeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "Ilość",
                    Values = new double[] { cntExp, cntInc, cntTrf }
                }
            };

            // Wykres wartości (wydatki/przychody/transfery)
            var sumExp = Rows.Where(x => x.Type == "Wydatek").Sum(x => (double)Math.Abs(x.Amount));
            var sumInc = Rows.Where(x => x.Type == "Przychód").Sum(x => (double)Math.Abs(x.Amount));
            var sumTrf = Rows.Where(x => x.Type == "Transfer").Sum(x => (double)Math.Abs(x.Amount)); // jw.

            OverviewAmountSeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "Wartość (zł)",
                    Values = new double[] { sumExp, sumInc, sumTrf }
                }
            };

            // Kategorie donuty też podpinamy już tutaj, żeby zakładka Kategorie miała dane
            BuildCategoryDonuts();
        }

        // =========================
        // 4) KATEGORIE – Donuty (3 osobne)
        // =========================
        private void BuildCategoryDonuts()
        {
            ExpenseDonutSeries = BuildDonutSeriesFor(Rows.Where(x => x.Type == "Wydatek"));
            IncomeDonutSeries = BuildDonutSeriesFor(Rows.Where(x => x.Type == "Przychód"));
            TransferDonutSeries = BuildDonutSeriesFor(Rows.Where(x => x.Type == "Transfer"));
        }

        private static ISeries[] BuildDonutSeriesFor(IEnumerable<ReportsService.ReportItem> rows)
        {
            var groups = rows
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                .Select(g => new { Name = g.Key, Total = g.Sum(x => Math.Abs(x.Amount)) })
                .OrderByDescending(x => x.Total)
                .Take(12) // ograniczamy wizualnie, reszta do tabeli później
                .ToList();

            if (groups.Count == 0)
                return Array.Empty<ISeries>();

            var series = new List<ISeries>();
            foreach (var g in groups)
            {
                series.Add(new PieSeries<double>
                {
                    Name = g.Name,
                    Values = new double[] { (double)g.Total }
                });
            }

            return series.ToArray();
        }

        // =========================
        // 2) BUDŻETY – analiza + wykres
        // =========================
        private void BuildBudgets(int uid)
        {
            Budgets.Clear();

            try
            {
                // Minimalnie: bierzemy budżety i liczymy "Spent" w okresie z Rows po kategorii = nazwa budżetu (jak miałaś wcześniej)
                var spentByCategory = Rows
                    .Where(r => r.Type == "Wydatek")
                    .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                    .ToDictionary(g => g.Key, g => g.Sum(x => Math.Abs(x.Amount)));

                var raw = BudgetService.GetBudgetsWithSummary(uid) ?? new List<BudgetService.BudgetSummary>();
                foreach (var b in raw)
                {
                    var planned = b.PlannedAmount;
                    var spent = b.Spent;

                    if (!string.IsNullOrWhiteSpace(b.Name) && spentByCategory.TryGetValue(b.Name, out var inPeriod))
                        spent = inPeriod;

                    Budgets.Add(new BudgetRow
                    {
                        Id = b.Id,
                        Name = b.Name ?? "(budżet)",
                        Planned = planned,
                        Spent = spent
                    });
                }
            }
            catch
            {
                // celowo cicho (UI ma działać nawet jak coś w bazie się nie spina)
            }

            // Wykres: Limit vs Wydano dla top N budżetów
            var top = Budgets
                .OrderByDescending(b => b.Planned)
                .Take(8)
                .ToList();

            BudgetsAxesX = new[] { new Axis { Labels = top.Select(x => x.Name).ToArray() } };
            BudgetsAxesY = new[] { new Axis { MinLimit = 0 } };

            BudgetsSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Wydano", Values = top.Select(x => (double)x.Spent).ToArray() },
                new ColumnSeries<double> { Name = "Limit",  Values = top.Select(x => (double)x.Planned).ToArray() }
            };
        }

        // =========================
        // 3) INWESTYCJE – placeholder + wykres
        // =========================
        private void BuildInvestments(int uid)
        {
            Investments.Clear();

            // Na teraz brak danych -> NIE zostawiamy pustych serii
            InvestmentsAxesX = new[] { new Axis { Labels = SafeLabels(null) } };
            InvestmentsAxesY = new[] { new Axis { MinLimit = 0 } };

            InvestmentsSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Zysk/Strata", Values = SafeValues(null) }
            };
        }


        // =========================
        // 5) KREDYTY – minimal + wykres
        // =========================
        private void BuildLoans(int uid)
        {
            Loans.Clear();

            // Tu docelowo podepniemy realne dane z Twojego modułu kredytów.
            // Teraz: nie ryzykujemy zależności od niepewnych metod – szkielet + wykres.
            try
            {
                var loans = DatabaseService.GetLoans(uid) ?? new List<LoanModel>();
                foreach (var l in loans)
                {
                    Loans.Add(new LoanRow
                    {
                        Id = l.Id,
                        Name = l.Name ?? "Kredyt",
                        RemainingToPay = 0m,
                        PaidInPeriod = 0m,
                        OverpaidInPeriod = 0m
                    });
                }
            }
            catch
            {
                // cicho
            }

            var labels = Loans.Select(x => x.Name).ToArray();
            LoansAxesX = new[] { new Axis { Labels = labels } };
            LoansAxesY = new[] { new Axis { MinLimit = 0 } };

            LoansSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Spłacono", Values = Loans.Select(x => (double)x.PaidInPeriod).ToArray() },
                new ColumnSeries<double> { Name = "Nadpłaty", Values = Loans.Select(x => (double)x.OverpaidInPeriod).ToArray() },
                new ColumnSeries<double> { Name = "Pozostało", Values = Loans.Select(x => (double)x.RemainingToPay).ToArray() }
            };
        }

        // =========================
        // 6) CELE – minimal + wykres
        // =========================
        private void BuildGoals(int uid)
        {
            Goals.Clear();

            try
            {
                var envGoals = DatabaseService.GetEnvelopeGoals(uid);
                if (envGoals != null)
                {
                    var periodLenDays = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);

                    foreach (var g in envGoals)
                    {
                        var row = new GoalRow
                        {
                            Name = g.Name ?? "Cel",
                            Target = g.Target,
                            Current = g.Allocated,
                            DueDate = g.Deadline
                        };

                        // Rekomendacja na kolejny okres:
                        // - jeśli cel ma deadline, liczymy "ile trzeba dołożyć na 1 kolejny okres", aby domknąć przed deadline
                        // - jeśli brak deadline, najprościej: brakujące = NeededNextPeriod (użytkownik widzi “ile brakuje”)
                        row.NeededNextPeriod = CalculateNeededForNextPeriod(row, periodLenDays);

                        Goals.Add(row);
                    }
                }
            }
            catch
            {
                // celowo cicho
            }

            // Wykres: postęp (%) dla top N celów
            var top = Goals
                .OrderByDescending(g => g.Target)
                .Take(10)
                .ToList();

            GoalsAxesX = new[] { new Axis { Labels = top.Select(x => x.Name).ToArray() } };
            GoalsAxesY = new[] { new Axis { MinLimit = 0, MaxLimit = 100 } };

            GoalsSeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "Postęp (%)",
                    Values = top.Select(x => (double)Math.Max(0, Math.Min(100, x.ProgressPercent))).ToArray()
                }
            };
        }

        private static decimal CalculateNeededForNextPeriod(GoalRow row, int periodLenDays)
        {
            if (row.Missing <= 0) return 0m;

            // jeśli nie ma deadline: prosta i czytelna rekomendacja
            if (row.DueDate == null)
                return row.Missing;

            var today = DateTime.Today;
            var due = row.DueDate.Value.Date;

            // jeśli termin już minął lub jest dziś: jedyna sensowna rekomendacja to “brakuje” (czyli wszystko)
            if (due <= today)
                return row.Missing;

            // ile dni zostało do deadline
            var daysLeft = (due - today).Days;

            // ile "okresów" tej długości jeszcze się mieści do deadline (zaokrąglamy w górę, żeby nie zaniżać)
            var periodsLeft = (int)Math.Ceiling(daysLeft / (double)Math.Max(1, periodLenDays));
            periodsLeft = Math.Max(1, periodsLeft);

            // rozkładamy brakującą kwotę na pozostałe okresy
            var perPeriod = row.Missing / periodsLeft;

            return Math.Max(0m, perPeriod);
        }

        // =========================
        // 7) SYMULACJA – planned tx w przyszłym okresie tej samej długości
        // =========================
        private void BuildPlannedSimulation(int uid)
        {
            PlannedSim.Clear();
            SimBalanceDelta = 0m;

            // Symulujemy przyszły zakres o tej samej długości jak wybrany okres:
            // [ToDate+1 .. ToDate+len]
            var len = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);
            var simFrom = ToDate.Date.AddDays(1);
            var simTo = simFrom.AddDays(len - 1);

            var planned = new List<PlannedRow>();

            try
            {
                using var con = DatabaseService.GetConnection();

                // Planned Expenses (ujemny wpływ na saldo)
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT e.Date AS TxDate,
       COALESCE(e.Description,'') AS TxDesc,
       e.Amount AS Amount
FROM Expenses e
WHERE e.UserId = @uid
  AND IFNULL(e.IsPlanned,0) = 1
  AND e.Date >= @from AND e.Date <= @to
ORDER BY e.Date;
";
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.Parameters.AddWithValue("@from", simFrom.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@to", simTo.ToString("yyyy-MM-dd"));

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var d = DateTime.Parse(r["TxDate"].ToString() ?? simFrom.ToString("yyyy-MM-dd"));
                        var desc = r["TxDesc"]?.ToString() ?? "";
                        var amount = Convert.ToDecimal(r["Amount"]);

                        planned.Add(new PlannedRow
                        {
                            Date = d,
                            Type = "Wydatek (plan)",
                            Description = desc,
                            Amount = -Math.Abs(amount)
                        });
                    }
                }

                // Planned Incomes (dodatni wpływ na saldo)
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT i.Date AS TxDate,
       COALESCE(i.Description,'') AS TxDesc,
       i.Amount AS Amount
FROM Incomes i
WHERE i.UserId = @uid
  AND IFNULL(i.IsPlanned,0) = 1
  AND i.Date >= @from AND i.Date <= @to
ORDER BY i.Date;
";
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.Parameters.AddWithValue("@from", simFrom.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@to", simTo.ToString("yyyy-MM-dd"));

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var d = DateTime.Parse(r["TxDate"].ToString() ?? simFrom.ToString("yyyy-MM-dd"));
                        var desc = r["TxDesc"]?.ToString() ?? "";
                        var amount = Convert.ToDecimal(r["Amount"]);

                        planned.Add(new PlannedRow
                        {
                            Date = d,
                            Type = "Przychód (plan)",
                            Description = desc,
                            Amount = Math.Abs(amount)
                        });
                    }
                }
            }
            catch
            {
                // cicho – symulacja ma nie psuć raportów, jeśli tabela/kolumna różni się u Ciebie
            }

            // Sort + wypełnij ObservableCollection
            foreach (var p in planned.OrderBy(x => x.Date))
                PlannedSim.Add(p);

            // Suma wpływu planowanych transakcji (delta salda)
            SimBalanceDelta = planned.Sum(x => x.Amount);

            // Wykres: saldo narastająco per dzień
            BuildSimulationChart(simFrom, simTo, planned);
        }

        private void BuildSimulationChart(DateTime simFrom, DateTime simTo, List<PlannedRow> planned)
        {
            // Grupujemy per dzień (netto)
            var daily = planned
                .GroupBy(p => p.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            var labels = new List<string>();
            var values = new List<double>();

            decimal running = 0m;

            // Żeby nie robić 365 etykiet w UI, ograniczamy do sensownego maksimum
            // (dla rocznych zakresów i tak w przyszłości zrobimy agregację miesięczną)
            var maxPoints = 62;

            var totalDays = (simTo.Date - simFrom.Date).Days + 1;
            if (totalDays <= maxPoints)
            {
                for (var d = simFrom.Date; d <= simTo.Date; d = d.AddDays(1))
                {
                    running += daily.TryGetValue(d, out var net) ? net : 0m;
                    labels.Add(d.ToString("dd.MM"));
                    values.Add((double)running);
                }
            }
            else
            {
                // agregacja tygodniowa (prosta, żeby wykres był czytelny)
                var cur = simFrom.Date;
                while (cur <= simTo.Date)
                {
                    var weekEnd = cur.AddDays(6);
                    if (weekEnd > simTo.Date) weekEnd = simTo.Date;

                    decimal weekNet = 0m;
                    for (var d = cur; d <= weekEnd; d = d.AddDays(1))
                        weekNet += daily.TryGetValue(d, out var net) ? net : 0m;

                    running += weekNet;

                    labels.Add($"{cur:dd.MM}-{weekEnd:dd.MM}");
                    values.Add((double)running);

                    cur = weekEnd.AddDays(1);
                }
            }

            SimulationAxesX = new[] { new Axis { Labels = labels.ToArray() } };
            SimulationAxesY = new[] { new Axis() };

            SimulationSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "Saldo (symulacja)",
                    Values = values.ToArray()
                }
            };
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


        // =========================
        // Eksport PDF – per zakładka (szkielet)
        // =========================
        private void ExportPdf(string? tabKey)
        {
            try
            {
                // Na dziś: PdfExportService masz już spięty pod całość.
                // Ten parametr tabKey zostawiamy jako “hak” – łatwo rozbudujesz usługę
                // aby generowała tylko 1 sekcję raportu na podstawie tabKey.
                //
                // Aktualnie: eksportuje całość (bezpiecznie, żeby działało od razu).
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