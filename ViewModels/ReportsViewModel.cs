using Finly.Helpers;
using Finly.Models;
using Finly.Pages;                 // GoalVm (masz to u siebie)
using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
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
                    Raise(nameof(PeriodLabel)); // <-- DOKŁADNIE TO DOPISZ
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
                    Raise(nameof(PeriodLabel)); // <-- DOKŁADNIE TO DOPISZ
                }
            }
        }


        public string PeriodLabel => $"{FromDate:dd.MM.yyyy} – {ToDate:dd.MM.yyyy}";

        // =========================
        // Filtry wspólne (wpływają na każdą zakładkę)
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
                // celowo cicho – filtry nie mogą wysypać strony
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
        // Dane wspólne (zaciągane raz, używane w wielu zakładkach)
        // =========================
        public ObservableCollection<ReportsService.ReportItem> Rows { get; }

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

        // Donut: słownik dla DonutChartControl (jak masz obecnie)
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

        // Trend (cashflow) – proste wiadra dzienne (VM gotowy pod wykres liniowy)
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

        // „Donut w kategoriach” + tabelki zależnie od filtra
        public ObservableCollection<CategoryAmount> CategoryBreakdown { get; }
        public ObservableCollection<CategoryAmount> ExpenseCategoriesSummary { get; }
        public ObservableCollection<CategoryAmount> IncomeCategoriesSummary { get; }
        public ObservableCollection<CategoryAmount> TransferCategoriesSummary { get; }

        // =========================
        // BUDŻETY
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
        public int BudgetsOkCount
        {
            get => _budgetsOkCount;
            private set { _budgetsOkCount = value; Raise(nameof(BudgetsOkCount)); }
        }

        private int _budgetsOverCount;
        public int BudgetsOverCount
        {
            get => _budgetsOverCount;
            private set { _budgetsOverCount = value; Raise(nameof(BudgetsOverCount)); }
        }

        // =========================
        // INWESTYCJE (placeholder – gotowe pod źródło danych)
        // =========================
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

        // =========================
        // KREDYTY
        // =========================
        public sealed class LoanRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Principal { get; set; }
            public decimal InterestRate { get; set; }
            public int TermMonths { get; set; }

            public decimal EstimatedMonthlyPayment { get; set; }
            public decimal PaidInPeriod { get; set; }        // do podpięcia z tabelą operacji
            public decimal OverpaidInPeriod { get; set; }    // do podpięcia z tabelą operacji
            public decimal RemainingToPay { get; set; }      // do podpięcia z harmonogramu / salda

            public string EstimatedMonthlyPaymentStr => EstimatedMonthlyPayment.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string PaidInPeriodStr => PaidInPeriod.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string OverpaidInPeriodStr => OverpaidInPeriod.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            public string RemainingToPayStr => RemainingToPay.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public ObservableCollection<LoanRow> Loans { get; }

        // =========================
        // CELE
        // =========================
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

            // „ile trzeba w kolejnym okresie”
            public decimal NeededNextPeriod { get; set; }
            public string NeededNextPeriodStr => NeededNextPeriod.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public ObservableCollection<GoalRow> Goals { get; }

        // =========================
        // SYMULACJA MAJĄTKU (planowane transakcje)
        // =========================
        public sealed class PlannedRow
        {
            public DateTime Date { get; set; }
            public string Type { get; set; } = ""; // Wydatek/Przychód/Transfer (jak podepniesz)
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

                // 1) bazowe transakcje (Rows) – respektuje filtry typu/kategorii/miejsca + okres
                LoadRows(uid);

                // 2) sumy globalne
                RecalcTotals();

                // 3) PRZEGLĄD
                BuildOverview();

                // 4) KATEGORIE (donut + tabelki zależnie od SelectedTransactionType)
                BuildCategories();

                // 5) BUDŻETY (analiza budżetów w kontekście wydatków z okresu)
                BuildBudgets(uid);

                // 6) KREDYTY
                BuildLoans(uid);

                // 7) CELE
                BuildGoals(uid);

                // 8) INWESTYCJE (na razie puste – gotowe do podpięcia)
                BuildInvestments(uid);

                // 9) SYMULACJA (planowane transakcje w przyszłość o długości wybranego okresu)
                BuildPlannedSimulation(uid);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd odświeżania raportów: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRows(int uid)
        {
            Rows.Clear();

            var rows = ReportsService.LoadReport(
                userId: uid,
                category: SelectedCategory,
                transactionType: SelectedTransactionType,
                from: FromDate,
                to: ToDate
            );

            foreach (var r in rows)
                Rows.Add(r);

            Raise(nameof(Rows));
        }


        private void RecalcTotals()
        {
            // Uwaga: w Rows przychód dodatni, wydatek dodatni? – w Twoim ReportsService: Wydatek ma Amount = e.Amount * -1
            // czyli Wydatek jest dodatni po stronie raportu? Nie – tam jest e.Amount * -1, a w DB e.Amount zwykle dodatnie,
            // więc w raporcie wyjdzie ujemny. Trzymamy logikę:
            var expenses = Rows.Where(r => r.Type == "Wydatek").Sum(r => Math.Abs(r.Amount));
            var incomes = Rows.Where(r => r.Type == "Przychód").Sum(r => Math.Abs(r.Amount));

            TotalExpenses = expenses;
            TotalIncomes = incomes;
            Balance = TotalIncomes - TotalExpenses;
        }

        // =========================
        // PRZEGLĄD
        // =========================
        private void BuildOverview()
        {
            OverviewTopExpenses.Clear();
            OverviewTopIncomes.Clear();
            Trend.Clear();

            // Top wydatki / przychody (po kwocie)
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

            // Donut (w przeglądzie) – domyślnie: według kategorii dla WYBRANEGO typu
            BuildDonutForCurrentMode();

            // Trend dzienny (prosty): incomes/expenses per dzień
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

            // Donut wg aktualnego SelectedTransactionType:
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

            // (A) Donut tabelka – zależnie od SelectedTransactionType
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

            // (B) Równolegle: trzy tabelki (wydatki / przychody / transfery) – ale UI może pokazywać tylko te,
            // które odpowiadają filtrowi (to już XAML/em)
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

            // Wydatki per kategoria w wybranym okresie – to jest baza do “spent”
            var spentByCategory = Rows
                .Where(r => r.Type == "Wydatek")
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                .ToDictionary(g => g.Key, g => g.Sum(x => Math.Abs(x.Amount)));

            // BudgetsService: nie znamy 1:1 mapowania budżetu->kategoria w Twoim modelu,
            // więc robimy bezpiecznie:
            // - jeżeli BudgetSummary ma Name odpowiadający nazwie kategorii – zadziała “jak złoto”
            // - jeżeli nie – dalej pokazujemy Planned/Spent z BudgetSummary (jeśli jest)
            try
            {
                var raw = BudgetService.GetBudgetsWithSummary(uid) ?? new List<BudgetService.BudgetSummary>();

                foreach (var b in raw)
                {
                    decimal planned = b.PlannedAmount;
                    decimal spent = b.Spent;

                    // jeśli umiemy policzyć “spent w okresie” po nazwie:
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
                // jeśli budżety nie działają – nie wysypujemy raportów
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
                    var monthly = LoansService.CalculateMonthlyPayment(l.Principal, l.InterestRate, l.TermMonths);

                    // Paid/Overpaid/Remaining – jeśli masz tabelę operacji kredytu:
                    // tutaj wstaw agregację po okresie (FromDate..ToDate).
                    // Na ten moment: zostawiamy 0 bezpiecznie, żeby UI działał i nie kłamał.
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
                // Prefer DB (koperty cele) – jest u Ciebie używane w innych miejscach
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

                        // „ile w kolejnym okresie”
                        // jeżeli cel nie dowieziony: rozkładamy brakującą kwotę na kolejny okres
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
        // INWESTYCJE (placeholder)
        // =========================
        private void BuildInvestments(int uid)
        {
            Investments.Clear();

            // Tu podepniesz źródło danych:
            // - tabela Investments / holdings / wartości dzienne
            // - albo integracja z API
            // VM jest gotowy, UI nie wybuchnie – po prostu będzie pusto.

            Raise(nameof(Investments));
        }

        // =========================
        // SYMULACJA: planowane transakcje w przyszłość (długość = wybrany okres)
        // =========================
        private void BuildPlannedSimulation(int uid)
        {
            PlannedSim.Clear();
            SimBalanceDelta = 0m;

            var periodLenDays = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);
            var simFrom = ToDate.Date.AddDays(1);
            var simTo = simFrom.AddDays(periodLenDays - 1);

            // Jeśli masz w DB IsPlanned w Expenses/Incomes – to pobieramy z SQL bezpośrednio.
            // Filtry: SelectedTransactionType + SelectedCategory.
            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();

                // Budujemy query warunkowo wg filtra typu:
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

                    // delta salda: przychód +, wydatek -
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
        // PDF export – docelowo ma brać dane z każdej zakładki zgodnie z filtrami
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
