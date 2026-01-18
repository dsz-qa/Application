using Finly.Helpers;
using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
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

            // WAŻNE: bez tego PreviousFrom/To zostają DateTime.MinValue i UI pokazuje "Brak danych"
            RecalcPreviousRange();

            Categories = new ObservableCollection<string>();
            MoneyPlaces = new ObservableCollection<string>();

            TransactionTypes = new ObservableCollection<string>
            {
                "Wszystko",
                "Wydatki",
                "Przychody",
                "Transfery"
            };

            // Zakładki – kolekcje
            OverviewTopExpenses = new ObservableCollection<TxLine>();
            OverviewTopIncomes = new ObservableCollection<TxLine>();

            CategoryBreakdown = new ObservableCollection<CategoryAmount>();
            ExpenseCategoriesSummary = new ObservableCollection<CategoryAmount>();
            IncomeCategoriesSummary = new ObservableCollection<CategoryAmount>();
            TransferCategoriesSummary = new ObservableCollection<CategoryAmount>();

            Budgets = new ObservableCollection<BudgetRow>();
            Loans = new ObservableCollection<LoanRow>();
            Goals = new ObservableCollection<GoalRow>();
            Investments = new ObservableCollection<InvestmentRow>();
            PlannedSim = new ObservableCollection<PlannedRow>();

            // Wspólne dane bazowe
            Rows = new ObservableCollection<ReportsService.ReportItem>();
            PreviousRows = new ObservableCollection<ReportsService.ReportItem>();

            RefreshCommand = new RelayCommand(_ => Refresh());
            ExportPdfCommand = new RelayCommand(_ => ExportPdf());
            ResetFiltersCommand = new RelayCommand(_ =>
            {
                ResetFilters();
                Refresh();
            });

            LoadFilters();
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
            // długość okresu w dniach (inkluzja obu końców)
            var len = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);

            // Poprzedni okres = dokładnie tyle samo dni bezpośrednio przed obecnym
            var prevTo = FromDate.Date.AddDays(-1);
            var prevFrom = prevTo.AddDays(-(len - 1));

            PreviousFrom = prevFrom;
            PreviousTo = prevTo;
        }

        // =========================
        // Filtry wspólne
        // =========================
        public ObservableCollection<string> Categories { get; }
        public ObservableCollection<string> MoneyPlaces { get; }
        public ObservableCollection<string> TransactionTypes { get; }

        private string _selectedCategory = "Wszystkie kategorie";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                var v = string.IsNullOrWhiteSpace(value) ? "Wszystkie kategorie" : value;
                if (_selectedCategory != v)
                {
                    _selectedCategory = v;
                    Raise(nameof(SelectedCategory));
                }
            }
        }

        private string _selectedMoneyPlace = "Wszystko";
        public string SelectedMoneyPlace
        {
            get => _selectedMoneyPlace;
            set
            {
                var v = string.IsNullOrWhiteSpace(value) ? "Wszystko" : value;
                if (_selectedMoneyPlace != v)
                {
                    _selectedMoneyPlace = v;
                    Raise(nameof(SelectedMoneyPlace));
                }
            }
        }

        private string _selectedTransactionType = "Wszystko";
        public string SelectedTransactionType
        {
            get => _selectedTransactionType;
            set
            {
                var v = string.IsNullOrWhiteSpace(value) ? "Wszystko" : value;
                if (_selectedTransactionType != v)
                {
                    _selectedTransactionType = v;
                    Raise(nameof(SelectedTransactionType));
                }
            }
        }

        public void ResetFilters()
        {
            SelectedCategory = Categories.FirstOrDefault() ?? "Wszystkie kategorie";
            SelectedMoneyPlace = MoneyPlaces.FirstOrDefault() ?? "Wszystko";
            SelectedTransactionType = TransactionTypes.FirstOrDefault() ?? "Wszystko";
        }

        private void LoadFilters()
        {
            try
            {
                var uid = UserService.GetCurrentUserId();
                if (uid <= 0) return;

                Categories.Clear();
                Categories.Add("Wszystkie kategorie");
                foreach (var c in DatabaseService.GetCategoriesByUser(uid) ?? new List<string>())
                    Categories.Add(c);

                MoneyPlaces.Clear();
                MoneyPlaces.Add("Wszystko");
                MoneyPlaces.Add("Wolna gotówka");
                MoneyPlaces.Add("Odłożona gotówka");

                foreach (var env in DatabaseService.GetEnvelopesNames(uid) ?? new List<string>())
                    MoneyPlaces.Add($"Koperta: {env}");

                foreach (var acc in DatabaseService.GetAccounts(uid) ?? new List<BankAccountModel>())
                    MoneyPlaces.Add($"Konto: {acc.AccountName}");

                ResetFilters();
            }
            catch
            {
                // celowo cicho
            }
        }

        // =========================
        // Komendy
        // =========================
        public ICommand RefreshCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ResetFiltersCommand { get; }

        // =========================
        // Zakładki – indeks
        // =========================
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
        public ObservableCollection<ReportsService.ReportItem> Rows { get; }

        // NOWE: poprzedni okres (osobna kolekcja, to samo źródło danych, inne daty)
        public ObservableCollection<ReportsService.ReportItem> PreviousRows { get; }

        // =========================
        // KPI – obecny okres
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
        public string TotalExpensesStr => TotalExpenses.ToString("N2", CultureInfo.CurrentCulture) + " zł";

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
        public string TotalIncomesStr => TotalIncomes.ToString("N2", CultureInfo.CurrentCulture) + " zł";

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
        public string BalanceStr => Balance.ToString("N2", CultureInfo.CurrentCulture) + " zł";

        // =========================
        // KPI – poprzedni okres (NOWE)
        // =========================
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
        public string PreviousTotalExpensesStr => PreviousTotalExpenses.ToString("N2", CultureInfo.CurrentCulture) + " zł";

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
        public string PreviousTotalIncomesStr => PreviousTotalIncomes.ToString("N2", CultureInfo.CurrentCulture) + " zł";

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
        public string PreviousBalanceStr => PreviousBalance.ToString("N2", CultureInfo.CurrentCulture) + " zł";

        // =========================
        // Porównanie (delta) – NOWE
        // =========================
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
        public string DeltaExpensesStr => FormatDeltaMoney(DeltaExpenses);

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
        public string DeltaIncomesStr => FormatDeltaMoney(DeltaIncomes);

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
        public string DeltaBalanceStr => FormatDeltaMoney(DeltaBalance);

        private static string FormatDeltaMoney(decimal value)
        {
            // + / - przed kwotą (czytelne porównanie)
            var sign = value > 0 ? "+" : value < 0 ? "-" : "";
            var abs = Math.Abs(value).ToString("N2", CultureInfo.CurrentCulture);
            return $"{sign}{abs} zł";
        }

        // =========================
        // Donut: (zostawiam jak masz)
        // =========================
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

        private decimal _chartTotalAll;
        public decimal ChartTotalAll
        {
            get => _chartTotalAll;
            private set
            {
                _chartTotalAll = value;
                Raise(nameof(ChartTotalAll));
            }
        }

        private string _selectedSliceInfo = "Kliknij kategorię na wykresie";
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

        public void UpdateSelectedSliceInfo(params object[] args)
        {
            if (args == null || args.Length == 0)
            {
                SelectedSliceInfo = "Kliknij kategorię na wykresie";
                return;
            }

            var parts = args
                .Where(a => a != null)
                .Select(a => a.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s));

            var text = string.Join(" • ", parts);
            SelectedSliceInfo = string.IsNullOrWhiteSpace(text)
                ? "Kliknij kategorię na wykresie"
                : text;
        }

        // =========================
        // PRZEGLĄD
        // =========================
        public sealed class TxLine
        {
            public DateTime Date { get; set; }
            public string Category { get; set; } = "";
            public string Description { get; set; } = "";
            public decimal Amount { get; set; }
            public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public ObservableCollection<TxLine> OverviewTopExpenses { get; }
        public ObservableCollection<TxLine> OverviewTopIncomes { get; }

        public sealed class TrendPoint
        {
            public DateTime Date { get; set; }
            public decimal Incomes { get; set; }
            public decimal Expenses { get; set; }
            public decimal Balance => Incomes - Expenses;
        }

        public ObservableCollection<TrendPoint> Trend { get; } = new();

        // =========================
        // KATEGORIE
        // =========================
        public sealed class CategoryAmount
        {
            public string Name { get; set; } = "";
            public decimal Amount { get; set; }
            public double SharePercent { get; set; }
        }

        public ObservableCollection<CategoryAmount> CategoryBreakdown { get; }
        public ObservableCollection<CategoryAmount> ExpenseCategoriesSummary { get; }
        public ObservableCollection<CategoryAmount> IncomeCategoriesSummary { get; }
        public ObservableCollection<CategoryAmount> TransferCategoriesSummary { get; }

        // =========================
        // BUDŻETY / KREDYTY / CELE / INWESTYCJE / SYMULACJA
        // (zostawiam jak masz – nie ruszam logiki, bo naprawa dotyczy poprzedniego okresu)
        // =========================
        public sealed class BudgetRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Planned { get; set; }
            public decimal Spent { get; set; }
            public decimal Remaining => Planned - Spent;
            public bool IsOver => Spent > Planned;

            public decimal UsedPercent => Planned <= 0 ? 0 : (Spent / Planned * 100m);

            public string PlannedStr => Planned.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string SpentStr => Spent.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string RemainingStr => Remaining.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string UsedPercentStr => UsedPercent.ToString("N1", CultureInfo.CurrentCulture) + " %";
        }

        public ObservableCollection<BudgetRow> Budgets { get; }

        private int _budgetsOkCount;
        public int BudgetsOkCount { get => _budgetsOkCount; private set { _budgetsOkCount = value; Raise(nameof(BudgetsOkCount)); } }

        private int _budgetsOverCount;
        public int BudgetsOverCount { get => _budgetsOverCount; private set { _budgetsOverCount = value; Raise(nameof(BudgetsOverCount)); } }

        public sealed class InvestmentRow
        {
            public string Name { get; set; } = "";
            public decimal StartValue { get; set; }
            public decimal EndValue { get; set; }
            public decimal Profit => EndValue - StartValue;

            public string StartValueStr => StartValue.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string EndValueStr => EndValue.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string ProfitStr => Profit.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public ObservableCollection<InvestmentRow> Investments { get; }

        public sealed class LoanRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Principal { get; set; }
            public decimal InterestRate { get; set; }
            public int TermMonths { get; set; }

            public decimal EstimatedMonthlyPayment { get; set; }
            public decimal PaidInPeriod { get; set; }
            public decimal OverpaidInPeriod { get; set; }
            public decimal RemainingToPay { get; set; }

            public string EstimatedMonthlyPaymentStr => EstimatedMonthlyPayment.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string PaidInPeriodStr => PaidInPeriod.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string OverpaidInPeriodStr => OverpaidInPeriod.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string RemainingToPayStr => RemainingToPay.ToString("N2", CultureInfo.CurrentCulture) + " zł";
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

            public string TargetStr => Target.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string CurrentStr => Current.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string ProgressPercentStr => ProgressPercent.ToString("N1", CultureInfo.CurrentCulture) + " %";
            public string MissingStr => Missing.ToString("N2", CultureInfo.CurrentCulture) + " zł";

            public decimal NeededNextPeriod { get; set; }
            public string NeededNextPeriodStr => NeededNextPeriod.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public ObservableCollection<GoalRow> Goals { get; }

        public sealed class PlannedRow
        {
            public DateTime Date { get; set; }
            public string Type { get; set; } = "";
            public string Description { get; set; } = "";
            public decimal Amount { get; set; }
            public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public ObservableCollection<PlannedRow> PlannedSim { get; }

        private decimal _simBalanceDelta;
        public decimal SimBalanceDelta
        {
            get => _simBalanceDelta;
            private set { _simBalanceDelta = value; Raise(nameof(SimBalanceDelta)); Raise(nameof(SimBalanceDeltaStr)); }
        }
        public string SimBalanceDeltaStr => SimBalanceDelta.ToString("N2", CultureInfo.CurrentCulture) + " zł";

        // =========================
        // GŁÓWNE ODŚWIEŻENIE
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
                LoadRowsInto(uid, FromDate, ToDate, Rows);

                // 2) Poprzedni okres (TEN SAM mechanizm, inne daty)
                LoadRowsInto(uid, PreviousFrom, PreviousTo, PreviousRows);

                // 3) KPI obecny/poprzedni + delty
                RecalcTotalsForBoth();

                // 4) PRZEGLĄD, KATEGORIE, reszta bazuje na Rows (obecny okres) – jak dotąd
                BuildOverview();
                BuildCategories();

                BuildBudgets(uid);
                BuildLoans(uid);
                BuildGoals(uid);
                BuildInvestments(uid);
                BuildPlannedSimulation(uid);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd odświeżania raportów: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRowsInto(int uid, DateTime from, DateTime to, ObservableCollection<ReportsService.ReportItem> target)
        {
            target.Clear();

            var rows = ReportsService.LoadReport(
                userId: uid,
                category: SelectedCategory,
                transactionType: SelectedTransactionType,
                from: from,
                to: to
            );

            foreach (var r in rows)
                target.Add(r);

            // pod binding
            if (ReferenceEquals(target, Rows))
                Raise(nameof(Rows));
            else
                Raise(nameof(PreviousRows));
        }

        private void RecalcTotalsForBoth()
        {
            // OBECNY
            var curExpenses = Rows.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var curIncomes = Rows.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            TotalExpenses = curExpenses;
            TotalIncomes = curIncomes;
            Balance = TotalIncomes - TotalExpenses;

            // POPRZEDNI
            var prevExpenses = PreviousRows.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var prevIncomes = PreviousRows.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            PreviousTotalExpenses = prevExpenses;
            PreviousTotalIncomes = prevIncomes;
            PreviousBalance = PreviousTotalIncomes - PreviousTotalExpenses;

            // DELTY (obecny - poprzedni)
            DeltaExpenses = TotalExpenses - PreviousTotalExpenses;
            DeltaIncomes = TotalIncomes - PreviousTotalIncomes;
            DeltaBalance = Balance - PreviousBalance;

            // Dodatkowo: upewnij się, że etykiety okresów są odświeżone
            Raise(nameof(PeriodLabel));
            Raise(nameof(PreviousPeriodLabel));
        }

        // =========================
        // PRZEGLĄD
        // =========================
        private void BuildOverview()
        {
            OverviewTopExpenses.Clear();
            OverviewTopIncomes.Clear();
            Trend.Clear();

            var topExp = Rows
                .Where(r => r.Type == "Wydatek")
                .OrderByDescending(r => Math.Abs(r.Amount))
                .Take(8)
                .Select(r => new TxLine
                {
                    Date = r.Date,
                    Category = r.Category,
                    Description = "",
                    Amount = Math.Abs(r.Amount)
                });

            foreach (var x in topExp) OverviewTopExpenses.Add(x);

            var topInc = Rows
                .Where(r => r.Type == "Przychód")
                .OrderByDescending(r => Math.Abs(r.Amount))
                .Take(8)
                .Select(r => new TxLine
                {
                    Date = r.Date,
                    Category = r.Category,
                    Description = "",
                    Amount = Math.Abs(r.Amount)
                });

            foreach (var x in topInc) OverviewTopIncomes.Add(x);

            BuildDonutForCurrentMode();

            var byDay = Rows
                .GroupBy(r => r.Date.Date)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var inc = g.Where(x => x.Type == "Przychód").Sum(x => Math.Abs(x.Amount));
                    var exp = g.Where(x => x.Type == "Wydatek").Sum(x => Math.Abs(x.Amount));
                    return new TrendPoint { Date = g.Key, Incomes = inc, Expenses = exp };
                });

            foreach (var p in byDay) Trend.Add(p);

            Raise(nameof(OverviewTopExpenses));
            Raise(nameof(OverviewTopIncomes));
            Raise(nameof(Trend));
        }

        private void BuildDonutForCurrentMode()
        {
            IEnumerable<ReportsService.ReportItem> scope = Rows;

            scope = SelectedTransactionType switch
            {
                "Wydatki" => scope.Where(r => r.Type == "Wydatek"),
                "Przychody" => scope.Where(r => r.Type == "Przychód"),
                "Transfery" => scope.Where(r => r.Type == "Transfer"),
                _ => scope
            };

            var groups = scope
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                .Select(g => new { Name = g.Key, Total = g.Sum(x => Math.Abs(x.Amount)) })
                .OrderByDescending(x => x.Total)
                .ToList();

            var total = groups.Sum(x => x.Total);

            ChartTotals = groups.ToDictionary(x => x.Name, x => x.Total);
            ChartTotalAll = total;

            SelectedSliceInfo = "Kliknij kategorię na wykresie";
        }

        // =========================
        // KATEGORIE
        // =========================
        private void BuildCategories()
        {
            CategoryBreakdown.Clear();
            ExpenseCategoriesSummary.Clear();
            IncomeCategoriesSummary.Clear();
            TransferCategoriesSummary.Clear();

            IEnumerable<ReportsService.ReportItem> scope = Rows;
            scope = SelectedTransactionType switch
            {
                "Wydatki" => scope.Where(r => r.Type == "Wydatek"),
                "Przychody" => scope.Where(r => r.Type == "Przychód"),
                "Transfery" => scope.Where(r => r.Type == "Transfer"),
                _ => scope
            };

            var groups = scope
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                .Select(g => new { Name = g.Key, Total = g.Sum(x => Math.Abs(x.Amount)) })
                .OrderByDescending(x => x.Total)
                .ToList();

            var total = groups.Sum(x => x.Total);

            foreach (var g in groups)
            {
                CategoryBreakdown.Add(new CategoryAmount
                {
                    Name = g.Name,
                    Amount = g.Total,
                    SharePercent = total > 0 ? (double)(g.Total / total * 100m) : 0.0
                });
            }

            FillCategoryTable(ExpenseCategoriesSummary, Rows.Where(r => r.Type == "Wydatek"));
            FillCategoryTable(IncomeCategoriesSummary, Rows.Where(r => r.Type == "Przychód"));
            FillCategoryTable(TransferCategoriesSummary, Rows.Where(r => r.Type == "Transfer"));

            Raise(nameof(CategoryBreakdown));
            Raise(nameof(ExpenseCategoriesSummary));
            Raise(nameof(IncomeCategoriesSummary));
            Raise(nameof(TransferCategoriesSummary));
        }

        private static void FillCategoryTable(ObservableCollection<CategoryAmount> target, IEnumerable<ReportsService.ReportItem> rows)
        {
            target.Clear();

            var groups = rows
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                .Select(g => new { Name = g.Key, Total = g.Sum(x => Math.Abs(x.Amount)) })
                .OrderByDescending(x => x.Total)
                .ToList();

            var total = groups.Sum(x => x.Total);

            foreach (var g in groups)
            {
                target.Add(new CategoryAmount
                {
                    Name = g.Name,
                    Amount = g.Total,
                    SharePercent = total > 0 ? (double)(g.Total / total * 100m) : 0.0
                });
            }
        }

        // =========================
        // BUDŻETY
        // =========================
        private void BuildBudgets(int uid)
        {
            Budgets.Clear();

            var spentByCategory = Rows
                .Where(r => r.Type == "Wydatek")
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                .ToDictionary(g => g.Key, g => g.Sum(x => Math.Abs(x.Amount)));

            try
            {
                var raw = BudgetService.GetBudgetsWithSummary(uid) ?? new List<BudgetService.BudgetSummary>();

                foreach (var b in raw)
                {
                    decimal planned = b.PlannedAmount;
                    decimal spent = b.Spent;

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
                // cicho
            }

            BudgetsOkCount = Budgets.Count(b => !b.IsOver);
            BudgetsOverCount = Budgets.Count(b => b.IsOver);

            Raise(nameof(Budgets));
        }

        // =========================
        // KREDYTY
        // =========================
        private void BuildLoans(int uid)
        {
            Loans.Clear();

            try
            {
                var loans = DatabaseService.GetLoans(uid) ?? new List<LoanModel>();

                foreach (var l in loans)
                {
                    // zostawiam jak było u Ciebie w projekcie (jeśli masz inną klasę, zmień tutaj)
                    var monthly = LoansService.CalculateMonthlyPayment(l.Principal, l.InterestRate, l.TermMonths);

                    Loans.Add(new LoanRow
                    {
                        Id = l.Id,
                        Name = l.Name ?? "Kredyt",
                        Principal = l.Principal,
                        InterestRate = l.InterestRate,
                        TermMonths = l.TermMonths,
                        EstimatedMonthlyPayment = monthly,
                        PaidInPeriod = 0m,
                        OverpaidInPeriod = 0m,
                        RemainingToPay = 0m
                    });
                }
            }
            catch
            {
                // cicho
            }

            Raise(nameof(Loans));
        }

        // =========================
        // CELE
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

                        row.NeededNextPeriod = row.Missing <= 0 ? 0 : (row.Missing / periodLenDays * periodLenDays);

                        Goals.Add(row);
                    }
                }
            }
            catch
            {
                // cicho
            }

            Raise(nameof(Goals));
        }

        // =========================
        // INWESTYCJE
        // =========================
        private void BuildInvestments(int uid)
        {
            Investments.Clear();
            Raise(nameof(Investments));
        }

        // =========================
        // SYMULACJA
        // =========================
        private void BuildPlannedSimulation(int uid)
        {
            PlannedSim.Clear();
            SimBalanceDelta = 0m;

            var periodLenDays = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);
            var simFrom = ToDate.Date.AddDays(1);
            var simTo = simFrom.AddDays(periodLenDays - 1);

            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();

                var parts = new List<string>();

                bool wantExpenses = SelectedTransactionType == "Wydatki" || SelectedTransactionType == "Wszystko";
                bool wantIncomes = SelectedTransactionType == "Przychody" || SelectedTransactionType == "Wszystko";

                if (wantExpenses)
                {
                    parts.Add(@"
SELECT e.Date as TxDate, 'Wydatek' as TxType, COALESCE(e.Description,'') as TxDesc, (e.Amount * -1) as Amount, COALESCE(c.Name,'(brak kategorii)') as CategoryName
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId=@u
  AND IFNULL(e.IsPlanned,0)=1
  AND e.Date>=@from AND e.Date<=@to
");
                }

                if (wantIncomes)
                {
                    parts.Add(@"
SELECT i.Date as TxDate, 'Przychód' as TxType, COALESCE(i.Description,'') as TxDesc, (i.Amount) as Amount, COALESCE(c.Name,'(brak kategorii)') as CategoryName
FROM Incomes i
LEFT JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId=@u
  AND IFNULL(i.IsPlanned,0)=1
  AND i.Date>=@from AND i.Date<=@to
");
                }

                if (parts.Count == 0)
                {
                    Raise(nameof(PlannedSim));
                    return;
                }

                var sql = $@"
SELECT * FROM (
{string.Join("\nUNION ALL\n", parts)}
) t
WHERE 1=1
";

                if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "Wszystkie kategorie")
                    sql += " AND t.CategoryName = @cat";

                sql += " ORDER BY t.TxDate ASC;";

                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@u", uid);
                cmd.Parameters.AddWithValue("@from", simFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", simTo.ToString("yyyy-MM-dd"));
                if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "Wszystkie kategorie")
                    cmd.Parameters.AddWithValue("@cat", SelectedCategory);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var d = DateTime.Parse(r["TxDate"].ToString() ?? simFrom.ToString("yyyy-MM-dd"));
                    var type = r["TxType"]?.ToString() ?? "";
                    var desc = r["TxDesc"]?.ToString() ?? "";
                    var amount = Convert.ToDecimal(r["Amount"]);

                    PlannedSim.Add(new PlannedRow
                    {
                        Date = d,
                        Type = type,
                        Description = desc,
                        Amount = amount
                    });

                    SimBalanceDelta += amount;
                }
            }
            catch
            {
                // cicho
            }

            Raise(nameof(PlannedSim));
        }

        // =========================
        // PDF
        // =========================
        private void ExportPdf()
        {
            try
            {
                var path = PdfExportService.ExportReportsPdf(this, null);
                ToastService.Success($"Raport PDF zapisano na pulpicie:\n{path}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"Błąd eksportu PDF: {ex.Message}");
            }
        }
    }
}
