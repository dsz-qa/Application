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

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl
    {
        private readonly int _uid;

        // Kolekcje do donutów
        public ObservableCollection<PieSlice> PieCurrent { get; } = new();
        public ObservableCollection<PieSlice> PieIncome { get; } = new();

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
            DataContext = this;

            // If PeriodBar exists in XAML ensure it's in sync and events are hooked
            if (FindName("PeriodBar") is Views.Controls.PeriodBarControl pb)
            {
                pb.RangeChanged += PeriodBar_RangeChanged;
                pb.SearchClicked += PeriodBar_SearchClicked;
                pb.ClearClicked += PeriodBar_ClearClicked;
            }

            // subscribe to DatabaseService data changes so we refresh planned list when new planned tx added
            DatabaseService.DataChanged += (_, __) => Dispatcher.BeginInvoke(new Action(() => LoadPlannedTransactions()), DispatcherPriority.Background);

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

            ApplyPreset(DateRangeMode.Day, DateTime.Today);
            RefreshMoneySummary();
            LoadCharts();
            LoadPlannedTransactions();
        }

        private void DashboardPage_Loaded(object? sender, RoutedEventArgs e)
        {
            // After initial layout, redraw charts/trend to ensure correct sizing
            LoadCharts();
            LoadPlannedTransactions();
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
        }

        private void NextPeriod_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(PresetOrder, _mode);
            if (idx <0) idx =0;

            idx = (idx +1) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);
            LoadCharts();
        }

        // Hooked handlers for PeriodBar:
        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            if (sender is Views.Controls.PeriodBarControl pb)
            {
                _mode = pb.Mode;
                _startDate = pb.StartDate;
                _endDate = pb.EndDate;
                LoadCharts();
                LoadPlannedTransactions();
            }
        }

        private void PeriodBar_SearchClicked(object? sender, EventArgs e)
        {
            if (sender is Views.Controls.PeriodBarControl pb)
            {
                _mode = pb.Mode;
                _startDate = pb.StartDate;
                _endDate = pb.EndDate;
                LoadCharts();
                LoadPlannedTransactions();
            }
        }

        private void PeriodBar_ClearClicked(object? sender, EventArgs e)
        {
            ApplyPreset(DateRangeMode.Day, DateTime.Today);
            LoadCharts();
            LoadPlannedTransactions();
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

            BuildExpensePie(expenses);
            BuildIncomePie(incomes);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                BindExpenseTable(expenses);
                BindIncomeTable(incomes);
                BindExpenseTrend(start, end);
            }), DispatcherPriority.Loaded);
        }

        private void LoadPlannedTransactions()
        {
            try
            {
                var plannedExpenses = DatabaseService.GetPlannedExpenses(_uid, start: null, end: null, limit:50) ?? new List<DatabaseService.CategoryTransactionDto>();
                var plannedIncomes = DatabaseService.GetPlannedIncomes(_uid, start: null, end: null, limit:50) ?? new List<DatabaseService.CategoryTransactionDto>();

                // Klucz: data (bez czasu) + kwota zaokrąglona do2 miejsc
                static string Key(DateTime d, decimal amount) => $"{d.Date:yyyy-MM-dd}|{Math.Round(Math.Abs(amount),2)}";

                var expDict = plannedExpenses
                    .GroupBy(e => Key(e.Date, e.Amount))
                    .ToDictionary(g => g.Key, g => g.ToList());
                var incDict = plannedIncomes
                    .GroupBy(i => Key(i.Date, i.Amount))
                    .ToDictionary(g => g.Key, g => g.ToList());

                var combined = new List<object>();

                // Wspólne klucze -> Transfer
                foreach (var key in expDict.Keys.Intersect(incDict.Keys))
                {
                    var e = expDict[key].First();
                    var i = incDict[key].First();
                    var desc = string.IsNullOrWhiteSpace(e.Description) ? i.Description : e.Description;
                    combined.Add(new { Date = e.Date, Description = desc, Amount = Math.Abs(e.Amount), Kind = "Transfer" });

                    expDict[key].RemoveAt(0);
                    incDict[key].RemoveAt(0);
                }

                // Pozostałe: zwykłe wpisy
                foreach (var list in expDict.Values)
                {
                    foreach (var e in list)
                        combined.Add(new { Date = e.Date, Description = e.Description, Amount = Math.Abs(e.Amount), Kind = "Wydatek" });
                }
                foreach (var list in incDict.Values)
                {
                    foreach (var i in list)
                        combined.Add(new { Date = i.Date, Description = i.Description, Amount = Math.Abs(i.Amount), Kind = "Przychód" });
                }

                combined = combined.OrderBy(x => (DateTime)x.GetType().GetProperty("Date")!.GetValue(x)!).ToList();

                if (FindName("PlannedTransactionsList") is ItemsControl pl)
                    pl.ItemsSource = combined;

                SetVisibility("PlannedEmptyText", combined.Count ==0);
                SetVisibility("PlannedTransactionsList", combined.Count >0);
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
            var list = (data ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>()).ToList();
            var sum = list.Sum(x => x.Amount);

            var rows = list
                .OrderByDescending(x => x.Amount)
                .Select(x => new TableRow
                {
                    Name = x.Name,
                    Amount = x.Amount,
                    Percent = sum >0 ? (double)(x.Amount / sum) *100.0 :0.0
                })
                .ToList();

            if (FindName("ExpenseTable") is ItemsControl expTable)
                expTable.ItemsSource = rows;

            if (FindName("TopCategoryBars") is ItemsControl catBars)
                catBars.ItemsSource = rows.Take(5).ToList();

            // Toggle empty placeholder / content
            SetVisibility("ExpenseEmptyText", rows.Count ==0);
            SetVisibility("ExpenseTable", rows.Count >0);
            SetVisibility("TopCategoryEmptyText", rows.Count ==0);
            SetVisibility("TopCategoryBars", rows.Count >0);
        }

        private void BindIncomeTable(IEnumerable<DatabaseService.CategoryAmountDto> data)
        {
            var list = (data ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>()).ToList();
            var sum = list.Sum(x => x.Amount);

            var rows = list
                .OrderByDescending(x => x.Amount)
                .Select(x => new TableRow
                {
                    Name = x.Name,
                    Amount = x.Amount,
                    Percent = sum >0 ? (double)(x.Amount / sum) *100.0 :0.0
                })
                .ToList();

            if (FindName("IncomeTable") is ItemsControl incTable)
                incTable.ItemsSource = rows;

            // Środek donuta przychodów – całość
            if (FindName("IncomeCenterNameText") is TextBlock n)
                n.Text = rows.Count ==0 ? "" : "Przychód";

            if (FindName("IncomeCenterValueText") is TextBlock v)
                v.Text = rows.Count ==0 ? "" : sum.ToString("N2", CultureInfo.CurrentCulture) + " zł";

            if (FindName("IncomeCenterPercentText") is TextBlock p)
                p.Text = rows.Count ==0 ? "" : "100,0% udziału";

            // Toggle empty placeholder / content
            SetVisibility("IncomeEmptyText", rows.Count ==0);
            SetVisibility("IncomeTable", rows.Count >0);
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
