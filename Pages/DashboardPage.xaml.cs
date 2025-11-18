using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl
    {
        private readonly int _uid;

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

        // suma wydatków – do obliczania % w środku donuta
        private decimal _currentTotalExpenses;

        public DashboardPage(int userId)
        {
            InitializeComponent();

            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;
            DataContext = this;

            ApplyPreset(DateRangeMode.Day, DateTime.Today);
            RefreshMoneySummary();
            LoadCharts();
        }

        public DashboardPage() : this(UserService.GetCurrentUserId()) { }

        // ===== KPI =====

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
            SetKpiText("InvestmentsText", 0m); // na razie 0
        }

        // ===== zakres dat =====

        private void ApplyPreset(DateRangeMode mode, DateTime anchor)
        {
            _mode = mode;

            switch (mode)
            {
                case DateRangeMode.Day:
                    _startDate = _endDate = anchor.Date;
                    break;
                case DateRangeMode.Week:
                    int diff = ((int)anchor.DayOfWeek + 6) % 7; // pon = 0
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

            if (StartPicker != null) StartPicker.SelectedDate = null;
            if (EndPicker != null) EndPicker.SelectedDate = null;
        }

        private void UpdatePeriodLabel()
        {
            if (FindName("PeriodLabelText") is not TextBlock label) return;

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

        private void PrevPeriod_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(PresetOrder, _mode);
            if (idx < 0) idx = 0;

            idx = (idx - 1 + PresetOrder.Length) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);
            LoadCharts();
        }

        private void NextPeriod_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(PresetOrder, _mode);
            if (idx < 0) idx = 0;

            idx = (idx + 1) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);
            LoadCharts();
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            if (StartPicker.SelectedDate is not DateTime s ||
                EndPicker.SelectedDate is not DateTime ed)
                return;

            if (s > ed) (s, ed) = (ed, s);

            _startDate = s.Date;
            _endDate = ed.Date;
            _mode = DateRangeMode.Custom;

            UpdatePeriodLabel();
            LoadCharts();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            StartPicker.SelectedDate = null;
            EndPicker.SelectedDate = null;

            ApplyPreset(DateRangeMode.Day, DateTime.Today);
            LoadCharts();
        }

        // ===== wykresy + tabelki =====

        private void LoadCharts()
        {
            DateTime start = _startDate == default ? DateTime.Today : _startDate;
            DateTime end = _endDate == default ? DateTime.Today : _endDate;
            if (start > end) (start, end) = (end, start);

            var expenses = DatabaseService.GetSpendingByCategorySafe(_uid, start, end);
            var incomes = DatabaseService.GetIncomeBySourceSafe(_uid, start, end);

            // true = koło wydatków (donut ze środkiem),
            // false = koło przychodów (bez aktualizacji środka)
            BuildPie(PieCurrent, expenses, isExpenseChart: true);
            BuildPie(PieIncome, incomes, isExpenseChart: false);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                BindExpenseTable(expenses);
                BindIncomeTable(incomes);
                BindExpenseTrend(start, end);
            }), DispatcherPriority.Loaded);
        }

        private void BuildPie(
            ObservableCollection<PieSlice> target,
            IEnumerable<DatabaseService.CategoryAmountDto> source,
            bool isExpenseChart)
        {
            target.Clear();

            var data = (source ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>())
                .Where(x => x.Amount > 0m)
                .OrderByDescending(x => x.Amount)
                .ToList();

            if (isExpenseChart)
            {
                if (data.Count == 0)
                {
                    _currentTotalExpenses = 0;
                    if (PieCenterNameText != null) PieCenterNameText.Text = "Brak danych";
                    if (PieCenterValueText != null) PieCenterValueText.Text = "0,00 zł";
                    if (PieCenterPercentText != null) PieCenterPercentText.Text = "";
                    return;
                }

                _currentTotalExpenses = data.Sum(x => x.Amount);

                if (PieCenterNameText != null) PieCenterNameText.Text = "Wszystko";
                if (PieCenterValueText != null) PieCenterValueText.Text =
                    _currentTotalExpenses.ToString("N2") + " zł";
                if (PieCenterPercentText != null) PieCenterPercentText.Text = "";
            }
            else
            {
                // przychody – brak danych => po prostu puste koło
                if (data.Count == 0)
                    return;
            }

            double startAngle = 0;
            int i = 0;

            const double centerX = 130;
            const double centerY = 130;
            const double outerRadius = 120;
            const double innerRadius = 70;

            decimal total = data.Sum(x => x.Amount);

            foreach (var item in data)
            {
                var sweep = (double)(item.Amount / total) * 360.0;
                if (sweep <= 0) continue;

                var slice = PieSlice.CreateDonut(
                    centerX, centerY,
                    innerRadius, outerRadius,
                    startAngle, sweep,
                    DefaultBrush(i++),
                    item.Name,
                    item.Amount);

                target.Add(slice);
                startAngle += sweep;
            }
        }

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
                    Percent = sum > 0 ? (double)(x.Amount / sum) * 100.0 : 0.0
                })
                .ToList();

            if (FindName("ExpenseTable") is ListView lv)
                lv.ItemsSource = rows;

            if (FindName("TopCategoryBars") is ItemsControl catBars)
                catBars.ItemsSource = rows.Take(5).ToList();
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
                    Percent = sum > 0 ? (double)(x.Amount / sum) * 100.0 : 0.0
                })
                .ToList();

            if (FindName("IncomeTable") is ListView lv)
                lv.ItemsSource = rows;
        }

        private void BindExpenseTrend(DateTime start, DateTime end)
        {
            if (FindName("ExpenseTrendBars") is not ItemsControl trendBars)
                return;

            try
            {
                var currentCats = DatabaseService.GetSpendingByCategorySafe(_uid, start, end)
                                   ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>();
                var currentTotal = currentCats.Sum(x => x.Amount);

                int days = (end.Date - start.Date).Days + 1;
                if (days <= 0) days = 1;

                var prevEnd = start.AddDays(-1);
                var prevStart = prevEnd.AddDays(-days + 1);

                var prevCats = DatabaseService.GetSpendingByCategorySafe(_uid, prevStart, prevEnd)
                               ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>();
                var prevTotal = prevCats.Sum(x => x.Amount);

                var max = Math.Max(currentTotal, prevTotal);
                if (max <= 0) max = 1;

                var items = new[]
                {
                    new ExpenseTrendItem
                    {
                        DateLabel = $"{prevStart:dd.MM}–{prevEnd:dd.MM}",
                        Amount    = prevTotal,
                        Percent   = (double)(prevTotal / max * 100m)
                    },
                    new ExpenseTrendItem
                    {
                        DateLabel = $"{start:dd.MM}–{end:dd.MM}",
                        Amount    = currentTotal,
                        Percent   = (double)(currentTotal / max * 100m)
                    }
                };

                trendBars.ItemsSource = items;
            }
            catch
            {
                trendBars.ItemsSource = Array.Empty<ExpenseTrendItem>();
            }
        }

        // ===== kliknięcie w kawałek donuta (wydatki) =====

        private void PieSlice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentTotalExpenses <= 0)
                return;

            if ((sender as FrameworkElement)?.DataContext is PieSlice slice)
            {
                var share = slice.Amount / _currentTotalExpenses * 100m;

                if (PieCenterNameText != null)
                    PieCenterNameText.Text = slice.Name;

                if (PieCenterValueText != null)
                    PieCenterValueText.Text = slice.Amount.ToString("N2") + " zł";

                if (PieCenterPercentText != null)
                    PieCenterPercentText.Text = $"{share:N1}% udziału";
            }
        }
    }

    // ===== klasy pomocnicze =====

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
            if (sweepAngle <= 0) sweepAngle = 0.1;
            if (sweepAngle >= 360) sweepAngle = 359.999;

            double a0 = DegToRad(startAngle - 90);
            double a1 = DegToRad(startAngle + sweepAngle - 90);

            // Punkty na zewnętrznym okręgu
            Point pOuter0 = new(centerX + outerRadius * Math.Cos(a0),
                                centerY + outerRadius * Math.Sin(a0));
            Point pOuter1 = new(centerX + outerRadius * Math.Cos(a1),
                                centerY + outerRadius * Math.Sin(a1));

            // Punkty na wewnętrznym okręgu
            Point pInner1 = new(centerX + innerRadius * Math.Cos(a1),
                                centerY + innerRadius * Math.Sin(a1));
            Point pInner0 = new(centerX + innerRadius * Math.Cos(a0),
                                centerY + innerRadius * Math.Sin(a0));

            bool large = sweepAngle > 180;

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pOuter0, isFilled: true, isClosed: true);

                // zewnętrzny łuk (zgodnie z ruchem wskazówek)
                ctx.ArcTo(pOuter1, new Size(outerRadius, outerRadius), 0,
                          large, SweepDirection.Clockwise, true, true);

                // linia do wewnętrznego okręgu
                ctx.LineTo(pInner1, true, true);

                // wewnętrzny łuk (przeciwnie do ruchu wskazówek)
                ctx.ArcTo(pInner0, new Size(innerRadius, innerRadius), 0,
                          large, SweepDirection.Counterclockwise, true, true);
                // zamknięcie figury wraca do pOuter0
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

        private static double DegToRad(double deg) => Math.PI / 180 * deg;
    }

    public sealed class TableRow
    {
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public string AmountStr => Amount.ToString("N2") + " zł";
        public double Percent { get; set; }
        public string PercentStr => Math.Round(Percent, 0) + "%";
    }

    public sealed class ExpenseTrendItem
    {
        public string DateLabel { get; set; } = "";
        public decimal Amount { get; set; }
        public string AmountStr => Amount.ToString("N2") + " zł";
        public double Percent { get; set; }
    }
}






























