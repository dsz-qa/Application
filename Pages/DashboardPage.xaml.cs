using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.ViewModels;
using Finly.Views.Dialogs;
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
        private readonly DashboardViewModel _vm;

        // zakres
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

        public DashboardPage(int userId)
        {
            InitializeComponent();

            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;
            _vm = new DashboardViewModel(_uid);
            DataContext = _vm;

            // Hook PeriodBar events
            if (FindName("PeriodBar") is Views.Controls.PeriodBarControl pb)
            {
                pb.RangeChanged += PeriodBar_RangeChanged;
                pb.SearchClicked += PeriodBar_SearchClicked;
                pb.ClearClicked += PeriodBar_ClearClicked;
            }

            // Refresh when DB changes
            DatabaseService.DataChanged += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _vm.LoadTransactions(_startDate == default ? DateTime.Today : _startDate,
                                     _endDate == default ? DateTime.Today : _endDate);

                LoadCharts();
                UpdateTablesVisibility();
            }), DispatcherPriority.Background);

            // redraw trend when canvas resizes
            if (FindName("ExpenseTrendCanvas") is Canvas canvas)
            {
                canvas.SizeChanged += (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                        BindExpenseTrend(_startDate == default ? DateTime.Today : _startDate,
                                        _endDate == default ? DateTime.Today : _endDate)),
                        DispatcherPriority.Background);
                };
            }

            // ensure charts refreshed when loaded (layout established)
            Loaded += DashboardPage_Loaded;

            // Default preset: Ten miesiąc
            ApplyPreset(DateRangeMode.Month, DateTime.Today);
            RefreshMoneySummary();
            LoadCharts();
            UpdateTablesVisibility();
        }

        public DashboardPage() : this(UserService.GetCurrentUserId()) { }

        private void DashboardPage_Loaded(object? sender, RoutedEventArgs e)
        {
            LoadCharts();
            UpdateTablesVisibility();
        }

        // =====================================================================
        // Podsumowanie okresu
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
            if (_uid <= 0) return;

            var snap = DatabaseService.GetMoneySnapshot(_uid);

            SetKpiText("TotalWealthText", snap.Total);
            SetKpiText("BanksText", snap.Banks);
            SetKpiText("FreeCashDashboardText", snap.Cash);
            SetKpiText("SavedToAllocateText", snap.SavedUnallocated);
            SetKpiText("EnvelopesDashboardText", snap.Envelopes);

            SetKpiText("InvestmentsText", 0m);
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
                    int diff = ((int)anchor.DayOfWeek + 6) % 7; // pon=0
                    _startDate = anchor.AddDays(-diff).Date;
                    _endDate = _startDate.AddDays(6);
                    break;

                case DateRangeMode.Month:
                    _startDate = new DateTime(anchor.Year, anchor.Month, 1);
                    _endDate = _startDate.AddMonths(1).AddDays(-1);
                    break;

                case DateRangeMode.Quarter:
                    int qStartMonth = (((anchor.Month - 1) / 3) * 3) + 1;
                    _startDate = new DateTime(anchor.Year, qStartMonth, 1);
                    _endDate = _startDate.AddMonths(3).AddDays(-1);
                    break;

                case DateRangeMode.Year:
                    _startDate = new DateTime(anchor.Year, 1, 1);
                    _endDate = new DateTime(anchor.Year, 12, 31);
                    break;

                case DateRangeMode.Custom:
                    break;
            }

            UpdatePeriodLabel();
        }

        private void UpdatePeriodLabel()
        {
            // Sync PeriodBar control with current state
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
            if (idx < 0) idx = 0;

            idx = (idx - 1 + PresetOrder.Length) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);

            LoadCharts();
            UpdateTablesVisibility();
        }

        private void NextPeriod_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(PresetOrder, _mode);
            if (idx < 0) idx = 0;

            idx = (idx + 1) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);

            LoadCharts();
            UpdateTablesVisibility();
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e) => ReloadForPeriodBar(sender);
        private void PeriodBar_SearchClicked(object? sender, EventArgs e) => ReloadForPeriodBar(sender);

        private void PeriodBar_ClearClicked(object? sender, EventArgs e)
        {
            ApplyPreset(DateRangeMode.Month, DateTime.Today);

            _vm.LoadTransactions(_startDate, _endDate);
            _vm.GenerateInsights(_startDate, _endDate);
            _vm.GenerateAlerts(_startDate, _endDate);
            _vm.GenerateForecast(_startDate, _endDate);
            _vm.RefreshCharts(_startDate, _endDate);

            LoadCharts();
            UpdateTablesVisibility();
        }

        private void ReloadForPeriodBar(object? sender)
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
            // prefer PeriodBar values
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

            // VM data
            _vm.LoadTransactions(start, end);
            _vm.GenerateInsights(start, end);
            _vm.GenerateAlerts(start, end);
            _vm.GenerateForecast(start, end);
            _vm.RefreshCharts(start, end);

            // NEW: draw donuts via DonutChartControl
            DrawDonuts(expenses, incomes);

            // period summary
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

        private void DrawDonuts(
            IEnumerable<DatabaseService.CategoryAmountDto>? expenses,
            IEnumerable<DatabaseService.CategoryAmountDto>? incomes)
        {
            var expDict = (expenses ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>())
                .Where(x => x.Amount > 0m)
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));

            var incDict = (incomes ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>())
                .Where(x => x.Amount > 0m)
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));

            var palette = GetPalette();

            decimal expTotal = expDict.Values.Sum();
            decimal incTotal = incDict.Values.Sum();

            if (FindName("ExpenseDonut") is Finly.Views.Controls.DonutChartControl expCtrl)
                expCtrl.Draw(expDict, expTotal, palette);

            if (FindName("IncomeDonut") is Finly.Views.Controls.DonutChartControl incCtrl)
                incCtrl.Draw(incDict, incTotal, palette);
        }


        private void UpdateTablesVisibility()
        {
            try
            {
                var incCount = _vm?.Incomes?.Count ?? 0;
                var expCount = _vm?.Expenses?.Count ?? 0;
                var plannedCount = _vm?.PlannedTransactions?.Count ?? 0;

                SetVisibility("IncomeEmptyText", incCount == 0);
                SetVisibility("IncomeTable", incCount > 0);

                SetVisibility("ExpenseEmptyText", expCount == 0);
                SetVisibility("ExpenseTable", expCount > 0);

                SetVisibility("PlannedEmptyText", plannedCount == 0);
                SetVisibility("PlannedTransactionsList", plannedCount > 0);
            }
            catch { }
        }

        // =====================================================================
        // Tabelki
        // =====================================================================
        private void BindExpenseTable(IEnumerable<DatabaseService.CategoryAmountDto> data)
        {
            var aggregatedList = (data ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>()).ToList();
            var sum = aggregatedList.Sum(x => x.Amount);

            var rows = aggregatedList
                .OrderByDescending(x => x.Amount)
                .Select(x => new TableRow
                {
                    Name = x.Name,
                    Amount = x.Amount,
                    Percent = sum > 0 ? (double)(x.Amount / sum) * 100.0 : 0.0
                })
                .ToList();

            if (FindName("TopCategoryBars") is ItemsControl catBars)
                catBars.ItemsSource = rows.Take(5).ToList();

            var detailedCount = _vm?.Expenses?.Count ?? 0;

            SetVisibility("ExpenseEmptyText", detailedCount == 0);
            SetVisibility("ExpenseTable", detailedCount > 0);

            SetVisibility("TopCategoryEmptyText", rows.Count == 0);
            SetVisibility("TopCategoryBars", rows.Count > 0);
        }

        private void BindIncomeTable(IEnumerable<DatabaseService.CategoryAmountDto> data)
        {
            // tylko widoczność (lista przychodów jest bindowana do _vm.Incomes przez XAML)
            var detailedCount = _vm?.Incomes?.Count ?? 0;

            SetVisibility("IncomeEmptyText", detailedCount == 0);
            SetVisibility("IncomeTable", detailedCount > 0);
        }

        // =====================================================================
        // Trend wydatków – agregacja
        // =====================================================================
        private enum TrendAggregateMode { Day, Week, Month }

        private List<ExpenseTrendItem> AggregateExpensesForTrend(DateTime dateFrom, DateTime dateTo, TrendAggregateMode mode)
        {
            DataTable dt = null;
            try { dt = DatabaseService.GetExpenses(_uid, dateFrom, dateTo); } catch { dt = null; }

            var rows = (dt == null) ? Enumerable.Empty<DataRow>() : dt.AsEnumerable();
            var items = new List<ExpenseTrendItem>();

            switch (mode)
            {
                case TrendAggregateMode.Day:
                    {
                        int days = (dateTo.Date - dateFrom.Date).Days + 1;
                        for (int i = 0; i < days; i++)
                        {
                            var d = dateFrom.Date.AddDays(i);
                            decimal sum = SumForDate(rows, d);
                            items.Add(new ExpenseTrendItem { DateLabel = d.ToString("dd.MM"), Amount = sum });
                        }
                        break;
                    }
                case TrendAggregateMode.Week:
                    {
                        var start = StartOfWeek(dateFrom);
                        var end = dateTo.Date;
                        int weekIndex = 1;

                        for (var cursor = start; cursor <= end; cursor = cursor.AddDays(7))
                        {
                            var wStart = cursor;
                            var wEnd = cursor.AddDays(6);
                            if (wEnd > end) wEnd = end;

                            decimal sum = SumForRange(rows, wStart, wEnd);
                            items.Add(new ExpenseTrendItem { DateLabel = $"Tydzień {weekIndex}", Amount = sum });
                            weekIndex++;
                        }
                        break;
                    }
                case TrendAggregateMode.Month:
                    {
                        var startMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
                        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

                        for (var cursor = startMonth; cursor <= endMonth; cursor = cursor.AddMonths(1))
                        {
                            var mStart = new DateTime(cursor.Year, cursor.Month, 1);
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

            var max = items.Count == 0 ? 0m : items.Max(i => i.Amount);
            if (max <= 0m) max = 1m;

            foreach (var it in items)
                it.Percent = (double)(it.Amount / max * 100m);

            return items;
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            int diff = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-diff).Date;
        }

        private static decimal SumForDate(IEnumerable<DataRow> rows, DateTime day)
        {
            decimal sum = 0m;
            foreach (var r in rows)
            {
                try
                {
                    var obj = r["Date"];
                    DateTime d;

                    if (obj is DateTime dtv) d = dtv;
                    else if (!DateTime.TryParse(obj?.ToString(), out d)) continue;

                    if (d.Date != day.Date) continue;

                    var amtObj = r["Amount"];
                    if (amtObj == DBNull.Value) continue;

                    sum += Math.Abs(Convert.ToDecimal(amtObj));
                }
                catch { }
            }
            return sum;
        }

        private static decimal SumForRange(IEnumerable<DataRow> rows, DateTime from, DateTime to)
        {
            decimal sum = 0m;
            foreach (var r in rows)
            {
                try
                {
                    var obj = r["Date"];
                    DateTime d;

                    if (obj is DateTime dtv) d = dtv;
                    else if (!DateTime.TryParse(obj?.ToString(), out d)) continue;

                    if (d.Date < from.Date || d.Date > to.Date) continue;

                    var amtObj = r["Amount"];
                    if (amtObj == DBNull.Value) continue;

                    sum += Math.Abs(Convert.ToDecimal(amtObj));
                }
                catch { }
            }
            return sum;
        }

        // =====================================================================
        // Trend wydatków – rysowanie
        // =====================================================================
        private void BindExpenseTrend(DateTime start, DateTime end)
        {
            if (FindName("ExpenseTrendCanvas") is not Canvas canvas ||
                FindName("ExpenseTrendLabels") is not ItemsControl labels)
                return;

            try
            {
                if (start > end) (start, end) = (end, start);

                TrendAggregateMode agg = TrendAggregateMode.Day;

                switch (_mode)
                {
                    case DateRangeMode.Day:
                    case DateRangeMode.Week:
                        agg = TrendAggregateMode.Day;
                        break;

                    case DateRangeMode.Month:
                    case DateRangeMode.Quarter:
                        agg = TrendAggregateMode.Week;
                        break;

                    case DateRangeMode.Year:
                        agg = TrendAggregateMode.Month;
                        break;

                    case DateRangeMode.Custom:
                        var days = (end - start).Days;
                        agg = days <= 31 ? TrendAggregateMode.Day : (days <= 180 ? TrendAggregateMode.Week : TrendAggregateMode.Month);
                        break;
                }

                var items = AggregateExpensesForTrend(start, end, agg);

                if (items.All(it => it.Amount == 0m))
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

                labels.ItemsSource = items;
                canvas.Children.Clear();

                double width = canvas.ActualWidth; if (width <= 0) width = 200;
                double height = canvas.ActualHeight; if (height <= 0) height = 180;

                var line = new Polyline
                {
                    Stroke = (Brush)Application.Current.TryFindResource("Brand.Green") ?? Brushes.LimeGreen,
                    StrokeThickness = 2
                };

                for (int i = 0; i < items.Count; i++)
                {
                    double x = (items.Count == 1) ? width / 2.0 : i * (width / (items.Count - 1));
                    double y = height - (items[i].Percent / 100.0) * (height - 4) - 2;

                    line.Points.Add(new Point(x, y));

                    var dot = new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = Brushes.White,
                        Stroke = line.Stroke,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(dot, x - 3);
                    Canvas.SetTop(dot, y - 3);
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
        // Paleta kolorów dla donutów
        // =====================================================================
        private Brush[] GetPalette()
        {
            return new Brush[]
            {
                new SolidColorBrush(Hex("#FFED7A1A")),
                new SolidColorBrush(Hex("#FF3FA7D6")),
                new SolidColorBrush(Hex("#FF7BC96F")),
                new SolidColorBrush(Hex("#FFAF7AC5")),
                new SolidColorBrush(Hex("#FFF6BF26")),
                new SolidColorBrush(Hex("#FF56C1A7")),
                new SolidColorBrush(Hex("#FFCE6A6B")),
                new SolidColorBrush(Hex("#FF9AA0A6")),
            };
        }

        private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s)!;

        // =====================================================================
        // Delete / Confirm (bez zmian)
        // =====================================================================
        private TransactionItem? GetSelectedIncome() => (FindName("IncomeTable") as ListBox)?.SelectedItem as TransactionItem;
        private TransactionItem? GetSelectedExpense() => (FindName("ExpenseTable") as ListBox)?.SelectedItem as TransactionItem;
        private TransactionItem? GetSelectedPlanned() => (FindName("PlannedTransactionsList") as ListBox)?.SelectedItem as TransactionItem;

        private void ReloadAfterEdit()
        {
            try
            {
                if (FindName("PeriodBar") is Views.Controls.PeriodBarControl pb)
                {
                    _mode = pb.Mode;
                    _startDate = pb.StartDate;
                    _endDate = pb.EndDate;
                }

                _vm.LoadTransactions(_startDate, _endDate);
                _vm.RefreshCharts(_startDate, _endDate);

                LoadCharts();
                UpdateTablesVisibility();
            }
            catch { }
        }

        private void ShowIncomeDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("IncomeDeleteConfirmPanel") is FrameworkElement p) p.Visibility = Visibility.Visible;
        }
        private void IncomeDeleteConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("IncomeDeleteConfirmPanel") is FrameworkElement p) p.Visibility = Visibility.Collapsed;
        }
        private void IncomeDeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedIncome();
            if (item == null) { ToastService.Info("Zaznacz pozycję do usunięcia."); return; }

            try { DatabaseService.DeleteIncome(item.Id); ToastService.Success("Usunięto przychód."); }
            catch (Exception ex) { ToastService.Error("Błąd usuwania przychodu.\n" + ex.Message); }
            finally { IncomeDeleteConfirmNo_Click(sender, e); ReloadAfterEdit(); }
        }

        private void ShowExpenseDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ExpenseDeleteConfirmPanel") is FrameworkElement p) p.Visibility = Visibility.Visible;
        }
        private void ExpenseDeleteConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ExpenseDeleteConfirmPanel") is FrameworkElement p) p.Visibility = Visibility.Collapsed;
        }
        private void ExpenseDeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedExpense();
            if (item == null) { ToastService.Info("Zaznacz pozycję do usunięcia."); return; }

            try { DatabaseService.DeleteExpense(item.Id); ToastService.Success("Usunięto wydatek."); }
            catch (Exception ex) { ToastService.Error("Błąd usuwania wydatku.\n" + ex.Message); }
            finally { ExpenseDeleteConfirmNo_Click(sender, e); ReloadAfterEdit(); }
        }

        private void ShowPlannedDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("PlannedDeleteConfirmPanel") is FrameworkElement p) p.Visibility = Visibility.Visible;
        }
        private void PlannedDeleteConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("PlannedDeleteConfirmPanel") is FrameworkElement p) p.Visibility = Visibility.Collapsed;
        }
        private void PlannedDeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedPlanned();
            if (item == null) { ToastService.Info("Zaznacz pozycję do usunięcia."); return; }

            try
            {
                if (string.Equals(item.Kind, "Przychód", StringComparison.OrdinalIgnoreCase))
                    DatabaseService.DeleteIncome(item.Id);
                else if (string.Equals(item.Kind, "Wydatek", StringComparison.OrdinalIgnoreCase))
                    DatabaseService.DeleteExpense(item.Id);
                else
                    ToastService.Info("Transfer usuń poprzez powiązane wpisy.");

                ToastService.Success("Usunięto.");
            }
            catch (Exception ex) { ToastService.Error("Błąd usuwania.\n" + ex.Message); }
            finally { PlannedDeleteConfirmNo_Click(sender, e); ReloadAfterEdit(); }
        }
    }

    // =====================================================================
    // Klasy pomocnicze (zostają, bo używasz ich w TopCategoryBars i trendzie)
    // =====================================================================
    public sealed class TableRow
    {
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        public double Percent { get; set; }
        public string PercentStr => Math.Round(Percent, 0).ToString(CultureInfo.CurrentCulture) + "%";
    }

    public sealed class ExpenseTrendItem
    {
        public string DateLabel { get; set; } = "";
        public decimal Amount { get; set; }
        public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        public double Percent { get; set; }
    }
}
