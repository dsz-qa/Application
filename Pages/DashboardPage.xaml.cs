using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Finly.ViewModels;

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl
    {
        private readonly int _uid;
        private readonly DashboardViewModel _vm;

        // Kolekcje do donutów
        public ObservableCollection<PieSlice> PieCurrent { get; } = new();
        public ObservableCollection<PieSlice> PieIncome { get; } = new();

        // =====================================================================
        // Podsumowanie okresu (nowa ramka)
        // =====================================================================
        private void RefreshPeriodSummary(decimal incomeSum, decimal expenseSum)
        {
            try
            {
                if (FindName("PeriodIncomeSummaryText") is TextBlock inc)
                    inc.Text = $"Przychody: {incomeSum:N2} zł";
                if (FindName("PeriodExpenseSummaryText") is TextBlock exp)
                    exp.Text = $"Wydatki: {expenseSum:N2} zł";
                if (FindName("PeriodBalanceSummaryText") is TextBlock bal)
                {
                    var balance = incomeSum - expenseSum;
                    bal.Text = $"Bilans: {balance:N2} zł";
                    bal.Foreground = balance >= 0
                        ? (Brush)Application.Current.TryFindResource("Brand.Green") ?? Brushes.LightGreen
                        : (Brush)Application.Current.TryFindResource("Brand.Red") ?? Brushes.IndianRed;
                }
            }
            catch { }
        }

        private static readonly DateRangeMode[] PresetOrder =
        {
            DateRangeMode.Day,
            DateRangeMode.Week,
            DateRangeMode.Month,
            DateRangeMode.Quarter,
            DateRangeMode.Year
        };

        private DateRangeMode _mode = DateRangeMode.Day;
        private DateTime _startDate;
        private DateTime _endDate;

        private decimal _currentTotalExpenses =0m;
        private decimal _currentTotalIncome =0m;

        public DashboardPage(int userId)
        {
            InitializeComponent();

            _uid = userId <=0 ? UserService.GetCurrentUserId() : userId;
            _vm = new DashboardViewModel(_uid);
            DataContext = _vm;

            // Hook PeriodBar events
            if (FindName("PeriodBar") is Views.Controls.PeriodBarControl pb)
            {
                pb.RangeChanged += PeriodBar_RangeChanged;
                pb.SearchClicked += PeriodBar_SearchClicked;
                pb.ClearClicked += PeriodBar_ClearClicked;
            }

            // subscribe to DatabaseService data changes so we refresh planned list when new planned tx added
            DatabaseService.DataChanged += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
            {
                // reload transactions for current range, then charts and visibilities
                _vm.LoadTransactions(_startDate == default ? DateTime.Today : _startDate,
                                     _endDate == default ? DateTime.Today : _endDate);
                LoadCharts();
                UpdateTablesVisibility();
            }), DispatcherPriority.Background);

            // redraw trend when canvas resizes (to avoid initial draw then disappearing after layout)
            if (FindName("ExpenseTrendCanvas") is Canvas canvas)
            {
                canvas.SizeChanged += (s, e) => {
                    // rebind trend on UI thread
                    Dispatcher.BeginInvoke(new Action(() => BindExpenseTrend(_startDate == default ? DateTime.Today : _startDate,
                                                                                 _endDate == default ? DateTime.Today : _endDate)), DispatcherPriority.Background);
                };
            }

            // ensure charts are refreshed once control is loaded (layout established)
            this.Loaded += DashboardPage_Loaded;

            // Default preset: Ten miesiąc
            ApplyPreset(DateRangeMode.Month, DateTime.Today);
            RefreshMoneySummary();
            LoadCharts();
            UpdateTablesVisibility();
        }

        private void DashboardPage_Loaded(object? sender, RoutedEventArgs e)
        {
            // After initial layout, redraw charts/trend to ensure correct sizing
            LoadCharts();
            UpdateTablesVisibility();
        }

        public DashboardPage() : this(UserService.GetCurrentUserId()) { }

        // =====================================================================
        // KPI
        // =====================================================================

        private void SetKpiText(string name, decimal value)
        {
            if (FindName(name) is TextBlock tb)
                tb.Text = value.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void RefreshMoneySummary()
        {
            if (_uid <=0) return;

            var snap = DatabaseService.GetMoneySnapshot(_uid);

            SetKpiText("TotalWealthText", snap.Total);
            SetKpiText("BanksText", snap.Banks);

            // TU BYŁ BŁĄD – użyj wolnej gotówki z MoneySnapshot
            SetKpiText("FreeCashDashboardText", snap.Cash);

            SetKpiText("SavedToAllocateText", snap.SavedUnallocated);
            SetKpiText("EnvelopesDashboardText", snap.Envelopes);

            SetKpiText("InvestmentsText",0m);
        }


        // =====================================================================
        // Zakres dat
        // =====================================================================

        private void ApplyPreset(DateRangeMode mode, DateTime anchor)
        {
            _mode = mode;

            switch (mode)
            {
                case DateRangeMode.Day:
                    _startDate = _endDate = anchor.Date;
                    break;
                case DateRangeMode.Week:
                    int diff = ((int)anchor.DayOfWeek +6) %7; // pon =0
                    _startDate = anchor.AddDays(-diff).Date;
                    _endDate = _startDate.AddDays(6);
                    break;
                case DateRangeMode.Month:
                    _startDate = new DateTime(anchor.Year, anchor.Month,1);
                    _endDate = _startDate.AddMonths(1).AddDays(-1);
                    break;
                case DateRangeMode.Quarter:
                    int qStartMonth = (((anchor.Month -1) /3) *3) +1;
                    _startDate = new DateTime(anchor.Year, qStartMonth,1);
                    _endDate = _startDate.AddMonths(3).AddDays(-1);
                    break;
                case DateRangeMode.Year:
                    _startDate = new DateTime(anchor.Year,1,1);
                    _endDate = new DateTime(anchor.Year,12,31);
                    break;
                case DateRangeMode.Custom:
                    break;
            }

            // Update PeriodBar control to reflect the selected preset/range
            UpdatePeriodLabel();
        }

        private void UpdatePeriodLabel()
        {
            // Sync PeriodBar control with current internal state
            if (FindName("PeriodBar") is Views.Controls.PeriodBarControl pb)
            {
                if (_mode == DateRangeMode.Custom)
                    pb.SetCustomRange(_startDate, _endDate);
                else
                    pb.SetPreset(_mode);
            }

            if (FindName("PeriodLabelText") is TextBlock label)
            {
                string text = _mode switch
                {
                    DateRangeMode.Day => "Dzisiaj",
                    DateRangeMode.Week => "Ten tydzień",
                    DateRangeMode.Month => "Ten miesiąc",
                    DateRangeMode.Quarter => "Ten kwartał",
                    DateRangeMode.Year => "Ten rok",
                    DateRangeMode.Custom => $"{_startDate:dd.MM.yyyy} – {_endDate:dd.MM.yyyy}",
                    _ => $"{_startDate:dd.MM.yyyy} – {_endDate:dd.MM.yyyy}"
                };
                label.Text = text;
            }
        }

        private void PrevPeriod_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(PresetOrder, _mode);
            if (idx <0) idx =0;

            idx = (idx -1 + PresetOrder.Length) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);
            LoadCharts();
            UpdateTablesVisibility();
        }

        private void NextPeriod_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(PresetOrder, _mode);
            if (idx <0) idx =0;

            idx = (idx +1) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);
            LoadCharts();
            UpdateTablesVisibility();
        }

        // Hooked handlers for PeriodBar:
        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            if (sender is Views.Controls.PeriodBarControl pb)
            {
                _mode = pb.Mode;
                _startDate = pb.StartDate;
                _endDate = pb.EndDate;
                _vm.LoadTransactions(_startDate, _endDate);
                _vm.GenerateInsights(_startDate, _endDate);
                _vm.GenerateAlerts(_startDate, _endDate);
                _vm.GenerateForecast(_startDate, _endDate);
                _vm.RefreshCharts(_startDate, _endDate);
                LoadCharts();
                UpdateTablesVisibility();
            }
        }

        private void PeriodBar_SearchClicked(object? sender, EventArgs e)
        {
            if (sender is Views.Controls.PeriodBarControl pb)
            {
                _mode = pb.Mode;
                _startDate = pb.StartDate;
                _endDate = pb.EndDate;
                _vm.LoadTransactions(_startDate, _endDate);
                _vm.GenerateInsights(_startDate, _endDate);
                _vm.GenerateAlerts(_startDate, _endDate);
                _vm.GenerateForecast(_startDate, _endDate);
                _vm.RefreshCharts(_startDate, _endDate);
                LoadCharts();
                UpdateTablesVisibility();
            }
        }

        private void PeriodBar_ClearClicked(object? sender, EventArgs e)
        {
            // When clearing, default to Month
            ApplyPreset(DateRangeMode.Month, DateTime.Today);
            _vm.LoadTransactions(_startDate, _endDate);
            _vm.GenerateInsights(_startDate, _endDate);
            _vm.GenerateAlerts(_startDate, _endDate);
            _vm.GenerateForecast(_startDate, _endDate);
            _vm.RefreshCharts(_startDate, _endDate);
            LoadCharts();
            UpdateTablesVisibility();
        }

        // Helper to set UI element visibility safely
        private void SetVisibility(string name, bool visible)
        {
            if (FindName(name) is UIElement el)
                el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // =====================================================================
        // Ładowanie wykresów + tabelek
        // =====================================================================

        private void LoadCharts()
        {
            // Prefer PeriodBar control values if available (ensure dashboard follows UI)
            if (FindName("PeriodBar") is Views.Controls.PeriodBarControl pb)
            {
                _mode = pb.Mode;
                _startDate = pb.StartDate;
                _endDate = pb.EndDate;
            }

            DateTime start = _startDate == default ? DateTime.Today : _startDate;
            DateTime end = _endDate == default ? DateTime.Today : _endDate;
            if (start > end) (start, end) = (end, start);

            var expenses = DatabaseService.GetSpendingByCategorySafe(_uid, start, end);
            var incomes = DatabaseService.GetIncomeBySourceSafe(_uid, start, end);

            // Update VM collections for tables and smart cards
            _vm.LoadTransactions(start, end);
            _vm.GenerateInsights(start, end);
            _vm.GenerateAlerts(start, end);
            _vm.GenerateForecast(start, end);
            _vm.RefreshCharts(start, end);

            BuildExpensePie(expenses);
            BuildIncomePie(incomes);

            // Compute sums for summary
            decimal incomeSum = (incomes ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>()).Sum(x => x.Amount);
            decimal expenseSum = (expenses ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>()).Sum(x => x.Amount);
            RefreshPeriodSummary(incomeSum, expenseSum);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                BindExpenseTable(expenses);
                BindIncomeTable(incomes);
                BindExpenseTrend(start, end);
                UpdateTablesVisibility();
            }), DispatcherPriority.Loaded);
        }

        private void UpdateTablesVisibility()
        {
            try
            {
                var incCount = _vm?.Incomes?.Count ??0;
                var expCount = _vm?.Expenses?.Count ??0;
                var plannedCount = _vm?.PlannedTransactions?.Count ??0;

                SetVisibility("IncomeEmptyText", incCount ==0);
                SetVisibility("IncomeTable", incCount >0);

                SetVisibility("ExpenseEmptyText", expCount ==0);
                SetVisibility("ExpenseTable", expCount >0);

                SetVisibility("PlannedEmptyText", plannedCount ==0);
                SetVisibility("PlannedTransactionsList", plannedCount >0);
            }
            catch { }
        }

        private sealed class PlannedTransactionRow
        {
            public DateTime Date { get; set; }
            public string DateDisplay => Date.ToString("dd.MM.yyyy");
            public decimal Amount { get; set; }
            public string AmountStr => Amount.ToString("N2") + " zł";
            public string Category { get; set; } = ""; // nazwa kategorii lub pusty
            public string Account { get; set; } = ""; // konto bankowe lub pusty
            public string Description { get; set; } = "";
            public string Kind { get; set; } = ""; // Przychód / Wydatek / Transfer
        }

        private void LoadPlannedTransactions()
        {
            try
            {
                // rely on ViewModel's PlannedTransactions (already populated by LoadTransactions)
                var planned = _vm?.PlannedTransactions ?? new ObservableCollection<TransactionItem>();
                if (FindName("PlannedTransactionsList") is ItemsControl pl) pl.ItemsSource = planned;
                SetVisibility("PlannedEmptyText", planned.Count ==0);
                SetVisibility("PlannedTransactionsList", planned.Count >0);
            }
            catch
            {
                SetVisibility("PlannedEmptyText", true);
                SetVisibility("PlannedTransactionsList", false);
            }
        }

        // =====================================================================
        // Donut WYDATKÓW
        // =====================================================================

        private void BuildExpensePie(IEnumerable<DatabaseService.CategoryAmountDto> source)
        {
            PieCurrent.Clear();

            var data = (source ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>())
                .Where(x => x.Amount >0m)
                .OrderByDescending(x => x.Amount)
                .ToList();

            if (data.Count ==0)
            {
                _currentTotalExpenses =0m;

                // Remove center texts so only the "Brak danych w tym okresie" placeholder is visible
                if (FindName("PieCenterNameText") is TextBlock n) n.Text = "";
                if (FindName("PieCenterValueText") is TextBlock v) v.Text = "";
                if (FindName("PieCenterPercentText") is TextBlock p) p.Text = "";

                // show empty placeholder inside the donut area (keep viewbox so placeholder is shown)
                SetVisibility("DonutExpenseEmptyText", true);
                SetVisibility("ExpenseDonutViewbox", true);
                return;
            }

            // hide empty placeholder, show donut
            SetVisibility("DonutExpenseEmptyText", false);
            SetVisibility("ExpenseDonutViewbox", true);

            var sum = data.Sum(x => x.Amount);
            _currentTotalExpenses = sum;

            if (FindName("PieCenterNameText") is TextBlock nAll) nAll.Text = "Wszystko";
            if (FindName("PieCenterValueText") is TextBlock vAll) vAll.Text = sum.ToString("N2") + " zł";
            if (FindName("PieCenterPercentText") is TextBlock pAll) pAll.Text = "";

            double startAngle =0;
            int colorIndex =0;

            const double centerX =110;
            const double centerY =110;
            const double outerRadius =100;
            const double innerRadius =60;

            foreach (var item in data)
            {
                var sweep = (double)(item.Amount / sum) *360.0;
                if (sweep <=0) continue;

                var slice = PieSlice.CreateDonut(
                    centerX, centerY,
                    innerRadius, outerRadius,
                    startAngle, sweep,
                    DefaultBrush(colorIndex++),
                    item.Name,
                    item.Amount);

                PieCurrent.Add(slice);
                startAngle += sweep;
            }
        }

        // =====================================================================
        // Donut PRZYCHODÓW
        // =====================================================================

        private void BuildIncomePie(IEnumerable<DatabaseService.CategoryAmountDto> source)
        {
            PieIncome.Clear();

            var data = (source ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>())
                .Where(x => x.Amount >0m)
                .OrderByDescending(x => x.Amount)
                .ToList();

            if (data.Count ==0)
            {
                _currentTotalIncome =0m;

                // Remove center texts so only the "Brak danych w tym okresie" placeholder is visible
                if (FindName("IncomeCenterNameText") is TextBlock n) n.Text = "";
                if (FindName("IncomeCenterValueText") is TextBlock v) v.Text = "";
                if (FindName("IncomeCenterPercentText") is TextBlock p) p.Text = "";

                // show empty placeholder inside the donut area (keep viewbox so placeholder is shown)
                SetVisibility("DonutIncomeEmptyText", true);
                SetVisibility("IncomeDonutViewbox", true);
                return;
            }

            // hide empty placeholder, show donut
            SetVisibility("DonutIncomeEmptyText", false);
            SetVisibility("IncomeDonutViewbox", true);

            var sum = data.Sum(x => x.Amount);
            _currentTotalIncome = sum;

            if (FindName("IncomeCenterNameText") is TextBlock nAll) nAll.Text = "Wszystko";
            if (FindName("IncomeCenterValueText") is TextBlock vAll) vAll.Text = sum.ToString("N2") + " zł";
            if (FindName("IncomeCenterPercentText") is TextBlock pAll) pAll.Text = "";

            double startAngle =0;
            int colorIndex =0;

            const double centerX =110;
            const double centerY =110;
            const double outerRadius =100;
            const double innerRadius =60;

            foreach (var item in data)
            {
                var sweep = (double)(item.Amount / sum) *360.0;
                if (sweep <=0) continue;

                var slice = PieSlice.CreateDonut(
                    centerX, centerY,
                    innerRadius, outerRadius,
                    startAngle, sweep,
                    DefaultBrush(colorIndex++),
                    item.Name,
                    item.Amount);

                PieIncome.Add(slice);
                startAngle += sweep;
            }
        }

        // =====================================================================
        // Tabelki
        // =====================================================================

        private void BindExpenseTable(IEnumerable<DatabaseService.CategoryAmountDto> data)
        {
            // data: zagregowane wydatki wg kategorii – potrzebne do TopCategoryBars i procentów
            var aggregatedList = (data ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>()).ToList();
            var sum = aggregatedList.Sum(x => x.Amount);

            var rows = aggregatedList
                .OrderByDescending(x => x.Amount)
                .Select(x => new TableRow
                {
                    Name = x.Name,
                    Amount = x.Amount,
                    Percent = sum >0 ? (double)(x.Amount / sum) *100.0 :0.0
                })
                .ToList();

            if (FindName("TopCategoryBars") is ItemsControl catBars)
                catBars.ItemsSource = rows.Take(5).ToList();

            // Widoczność oparta na szczegółowych transakcjach
            var detailedCount = _vm?.Expenses?.Count ??0;
            SetVisibility("ExpenseEmptyText", detailedCount ==0);
            SetVisibility("ExpenseTable", detailedCount >0);
            SetVisibility("TopCategoryEmptyText", rows.Count ==0);
            SetVisibility("TopCategoryBars", rows.Count >0);
        }

        private void BindIncomeTable(IEnumerable<DatabaseService.CategoryAmountDto> data)
        {
            // data: zagregowane przychody (np. wg źródła) – wykorzystujemy tylko do sumy i centrum donuta
            var aggregatedList = (data ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>()).ToList();
            var sum = aggregatedList.Sum(x => x.Amount);

            // NIE nadpisujemy ItemsSource – XAML ma już ItemsSource="{Binding DataContext.Incomes, ElementName=Root}"
            // Wyświetlane wiersze mają korzystać z kolekcji szczegółowych transakcji (_vm.Incomes) z właściwościami:
            // DateDisplay, AmountStr, Category, Account, Description.
            var detailed = _vm?.Incomes;
            int detailedCount = detailed?.Count ??0;

            // Ustaw teksty w środku donuta (jak wcześniej – na podstawie zagregowanych wartości)
            if (FindName("IncomeCenterNameText") is TextBlock n)
                n.Text = aggregatedList.Count ==0 ? "" : "Przychód";
            if (FindName("IncomeCenterValueText") is TextBlock v)
                v.Text = aggregatedList.Count ==0 ? "" : sum.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            if (FindName("IncomeCenterPercentText") is TextBlock p)
                p.Text = aggregatedList.Count ==0 ? "" : "100,0% udziału";

            // Widoczność: oparta na szczegółowych danych, nie na agregacji
            SetVisibility("IncomeEmptyText", detailedCount ==0);
            SetVisibility("IncomeTable", detailedCount >0);
        }

        // =====================================================================
        // Agregacja na potrzeby trendu (day/week/month)
        // =====================================================================

        private enum TrendAggregateMode { Day, Week, Month }

        private List<ExpenseTrendItem> AggregateExpensesForTrend(DateTime dateFrom, DateTime dateTo, TrendAggregateMode mode)
        {
            // Bezpieczne pobranie danych (DataTable z kolumnami Date, Amount)
            DataTable dt = null;
            try { dt = DatabaseService.GetExpenses(_uid, dateFrom, dateTo); } catch { dt = null; }
            var rows = (dt == null) ? Enumerable.Empty<DataRow>() : dt.AsEnumerable();

            var items = new List<ExpenseTrendItem>();

            switch (mode)
            {
                case TrendAggregateMode.Day:
                {
                    int days = (dateTo.Date - dateFrom.Date).Days +1;
                    for (int i =0; i < days; i++)
                    {
                        var d = dateFrom.Date.AddDays(i);
                        decimal sum = SumForDate(rows, d);
                        items.Add(new ExpenseTrendItem
                        {
                            DateLabel = d.ToString("dd.MM"),
                            Amount = sum
                        });
                    }
                    break;
                }
                case TrendAggregateMode.Week:
                {
                    // Tygodnie liczymy od poniedziałku; numerujemy lokalnie od1 w obrębie zakresu
                    var start = StartOfWeek(dateFrom);
                    var end = dateTo.Date;
                    int weekIndex =1;
                    for (var cursor = start; cursor <= end; cursor = cursor.AddDays(7))
                    {
                        var wStart = cursor;
                        var wEnd = cursor.AddDays(6);
                        if (wEnd > end) wEnd = end;
                        decimal sum = SumForRange(rows, wStart, wEnd);
                        items.Add(new ExpenseTrendItem
                        {
                            DateLabel = $"Tydzień {weekIndex}",
                            Amount = sum
                        });
                        weekIndex++;
                    }
                    break;
                }
                case TrendAggregateMode.Month:
                {
                    var startMonth = new DateTime(dateFrom.Year, dateFrom.Month,1);
                    var endMonth = new DateTime(dateTo.Year, dateTo.Month,1);
                    for (var cursor = startMonth; cursor <= endMonth; cursor = cursor.AddMonths(1))
                    {
                        var mStart = new DateTime(cursor.Year, cursor.Month,1);
                        var mEnd = mStart.AddMonths(1).AddDays(-1);
                        if (mEnd > dateTo) mEnd = dateTo;
                        decimal sum = SumForRange(rows, mStart, mEnd);
                        items.Add(new ExpenseTrendItem
                        {
                            DateLabel = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(cursor.Month),
                            Amount = sum
                        });
                    }
                    break;
                }
            }

            // Oblicz normalizację procentową względem maksimum
            var max = items.Count ==0 ?0m : items.Max(i => i.Amount);
            if (max <=0m) max =1m;
            foreach (var it in items)
                it.Percent = (double)(it.Amount / max *100m);

            return items;
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            int diff = ((int)date.DayOfWeek +6) %7; // Monday=0
            return date.AddDays(-diff).Date;
        }

        private static decimal SumForDate(IEnumerable<DataRow> rows, DateTime day)
        {
            decimal sum =0m;
            foreach (var r in rows)
            {
                try
                {
                    var obj = r["Date"]; DateTime d;
                    if (obj is DateTime dtv) d = dtv; else if (!DateTime.TryParse(obj?.ToString(), out d)) continue;
                    if (d.Date != day.Date) continue;
                    var amtObj = r["Amount"]; if (amtObj == DBNull.Value) continue; sum += Math.Abs(Convert.ToDecimal(amtObj));
                }
                catch { }
            }
            return sum;
        }

        private static decimal SumForRange(IEnumerable<DataRow> rows, DateTime from, DateTime to)
        {
            decimal sum =0m;
            foreach (var r in rows)
            {
                try
                {
                    var obj = r["Date"]; DateTime d;
                    if (obj is DateTime dtv) d = dtv; else if (!DateTime.TryParse(obj?.ToString(), out d)) continue;
                    if (d.Date < from.Date || d.Date > to.Date) continue;
                    var amtObj = r["Amount"]; if (amtObj == DBNull.Value) continue; sum += Math.Abs(Convert.ToDecimal(amtObj));
                }
                catch { }
            }
            return sum;
        }

        // =====================================================================
        // Trend wydatków – wykres liniowy
        // =====================================================================

        private void BindExpenseTrend(DateTime start, DateTime end)
        {
            if (FindName("ExpenseTrendCanvas") is not Canvas canvas ||
                FindName("ExpenseTrendLabels") is not ItemsControl labels)
                return;

            try
            {
                if (start > end) (start, end) = (end, start);

                // Ustal agregację zależnie od presetów
                TrendAggregateMode agg = TrendAggregateMode.Day;
                switch (_mode)
                {
                    case DateRangeMode.Day:
                    case DateRangeMode.Week:
                        agg = TrendAggregateMode.Day; // dla tygodnia – dzienne punkty
                        break;
                    case DateRangeMode.Month:
                    case DateRangeMode.Quarter:
                        agg = TrendAggregateMode.Week; // tygodniowo
                        break;
                    case DateRangeMode.Year:
                        agg = TrendAggregateMode.Month; // miesięcznie
                        break;
                    case DateRangeMode.Custom:
                        // heurystyka: jeśli zakres >60 dni -> tygodnie, >200 dni -> miesiące
                        var days = (end - start).Days;
                        agg = days <=31 ? TrendAggregateMode.Day : (days <=180 ? TrendAggregateMode.Week : TrendAggregateMode.Month);
                        break;
                }

                var items = AggregateExpensesForTrend(start, end, agg);

                // Jeśli wszystko zero – placeholder
                if (items.All(it => it.Amount ==0m))
                {
                    canvas.Children.Clear();
                    labels.ItemsSource = Array.Empty<ExpenseTrendItem>();
                    SetVisibility("ExpenseTrendEmptyText", true);
                    SetVisibility("ExpenseTrendCanvas", true);
                    SetVisibility("ExpenseTrendLabels", false);
                    return;
                }

                SetVisibility("ExpenseTrendEmptyText", false);
                SetVisibility("ExpenseTrendCanvas", true);
                SetVisibility("ExpenseTrendLabels", true);

                // Etykiety – bez zlewania
                labels.ItemsSource = items;

                // Rysowanie linii
                canvas.Children.Clear();

                double width = canvas.ActualWidth; if (width <=0) width =200;
                double height = canvas.ActualHeight; if (height <=0) height =180;

                var line = new Polyline
                {
                    Stroke = (Brush)Application.Current.TryFindResource("Brand.Green") ?? Brushes.LimeGreen,
                    StrokeThickness =2
                };

                for (int i =0; i < items.Count; i++)
                {
                    double x = (items.Count ==1) ? width /2.0 : i * (width / (items.Count -1));
                    double y = height - (items[i].Percent /100.0) * (height -4) -2;

                    line.Points.Add(new Point(x, y));

                    var dot = new Ellipse
                    {
                        Width =6,
                        Height =6,
                        Fill = Brushes.White,
                        Stroke = line.Stroke,
                        StrokeThickness =1
                    };
                    Canvas.SetLeft(dot, x -3);
                    Canvas.SetTop(dot, y -3);
                    canvas.Children.Add(dot);
                }

                canvas.Children.Add(line);
            }
            catch
            {
                canvas.Children.Clear();
                labels.ItemsSource = Array.Empty<ExpenseTrendItem>();
            }
        }

        // =====================================================================
        // Kliknięcia w kawałki donuta
        // =====================================================================

        // Stary handler z XAML – deleguje do wersji „wydatkowej”
        private void PieSlice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => ExpensePieSlice_MouseLeftButtonDown(sender, e);

        private void ExpensePieSlice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentTotalExpenses <=0)
                return;

            if ((sender as FrameworkElement)?.DataContext is PieSlice slice)
            {
                var share = slice.Amount / _currentTotalExpenses *100m;

                if (FindName("PieCenterNameText") is TextBlock n)
                    n.Text = slice.Name;
                if (FindName("PieCenterValueText") is TextBlock v)
                    v.Text = slice.Amount.ToString("N2") + " zł";
                if (FindName("PieCenterPercentText") is TextBlock p)
                    p.Text = $"{share:N1}% udziału";
            }
        }

        private void IncomePieSlice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentTotalIncome <=0)
                return;

            if ((sender as FrameworkElement)?.DataContext is PieSlice slice)
            {
                var share = slice.Amount / _currentTotalIncome *100m;

                if (FindName("IncomeCenterNameText") is TextBlock n)
                    n.Text = slice.Name;
                if (FindName("IncomeCenterValueText") is TextBlock v)
                    v.Text = slice.Amount.ToString("N2") + " zł";
                if (FindName("IncomeCenterPercentText") is TextBlock p)
                    p.Text = $"{share:N1}% udziału";
            }
        }

        // =====================================================================
        // Paleta kolorów
        // =====================================================================

        private Brush DefaultBrush(int i)
        {
            Color[] palette =
            {
                Hex("#FFED7A1A"),
                Hex("#FF3FA7D6"),
                Hex("#FF7BC96F"),
                Hex("#FFAF7AC5"),
                Hex("#FFF6BF26"),
                Hex("#FF56C1A7"),
                Hex("#FFCE6A6B"),
                Hex("#FF9AA0A6")
            };
            return new SolidColorBrush(palette[i % palette.Length]);
        }

        private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s)!;
    }

    // =====================================================================
    // Klasy pomocnicze
    // =====================================================================

    public sealed class PieSlice
    {
        public string Name { get; init; } = "";
        public decimal Amount { get; init; }
        public Brush Brush { get; init; } = Brushes.Gray;
        public Geometry Geometry { get; init; } = Geometry.Empty;

        public static PieSlice CreateDonut(
            double centerX, double centerY,
            double innerRadius, double outerRadius,
            double startAngle, double sweepAngle,
            Brush brush, string name, decimal amount)
        {
            if (sweepAngle <=0) sweepAngle =0.1;
            if (sweepAngle >=360) sweepAngle =359.999;

            double a0 = DegToRad(startAngle -90);
            double a1 = DegToRad(startAngle + sweepAngle -90);

            Point pOuter0 = new(centerX + outerRadius * Math.Cos(a0),
                                centerY + outerRadius * Math.Sin(a0));
            Point pOuter1 = new(centerX + outerRadius * Math.Cos(a1),
                                centerY + outerRadius * Math.Sin(a1));

            Point pInner1 = new(centerX + innerRadius * Math.Cos(a1),
                                centerY + innerRadius * Math.Sin(a1));
            Point pInner0 = new(centerX + innerRadius * Math.Cos(a0),
                                centerY + innerRadius * Math.Sin(a0));

            bool large = sweepAngle >180;

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pOuter0, isFilled: true, isClosed: true);

                ctx.ArcTo(pOuter1, new Size(outerRadius, outerRadius),0,
                          large, SweepDirection.Clockwise, true, true);

                ctx.LineTo(pInner1, true, true);

                ctx.ArcTo(pInner0, new Size(innerRadius, innerRadius),0,
                          large, SweepDirection.Counterclockwise, true, true);
            }
            g.Freeze();

            return new PieSlice
            {
                Name = name,
                Amount = amount,
                Brush = brush,
                Geometry = g
            };
        }

        private static double DegToRad(double deg) => Math.PI /180 * deg;
    }

    public sealed class TableRow
    {
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public string AmountStr => Amount.ToString("N2") + " zł";
        public double Percent { get; set; }
        public string PercentStr => Math.Round(Percent,0) + "%";
    }

    public sealed class ExpenseTrendItem
    {
        public string DateLabel { get; set; } = "";
        public decimal Amount { get; set; }
        public string AmountStr => Amount.ToString("N2") + " zł";
        public double Percent { get; set; }
    }
}
