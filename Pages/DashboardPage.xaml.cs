using Finly.Models;
using Finly.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl
    {
        private readonly int _uid;

        public ObservableCollection<PieSlice> PieCurrent { get; } = new();
        public ObservableCollection<PieSlice> PieIncome { get; } = new();

        // Presety okresu
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
            _uid = userId;
            DataContext = this;

            Loaded += DashboardPage_Loaded;
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyPreset(DateRangeMode.Day, DateTime.Today);
            RefreshMoneySummary();
            LoadCharts();
        }

        // ========================= KPI =========================

        private void SetKpiText(string name, decimal value)
        {
            if (FindName(name) is TextBlock tb)
                tb.Text = value.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void RefreshMoneySummary()
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0) return;

            var snap = DatabaseService.GetMoneySnapshot(uid);

            SetKpiText("TotalWealthText", snap.Total);
            SetKpiText("BanksText", snap.Banks);
            SetKpiText("FreeCashDashboardText", snap.Cash);
            SetKpiText("SavedToAllocateText", snap.SavedUnallocated);
            SetKpiText("EnvelopesDashboardText", snap.Envelopes);

            // Inwestycje – na razie 0
            SetKpiText("InvestmentsText", 0m);
        }

        // ========================= OKRES DAT =========================

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
                    // ręcznie z kalendarzy
                    break;
            }

            UpdatePeriodLabel();

            var startPicker = FindName("StartPicker") as DatePicker;
            var endPicker = FindName("EndPicker") as DatePicker;

            if (startPicker != null)
                startPicker.SelectedDate = _startDate;
            if (endPicker != null)
                endPicker.SelectedDate = _endDate;
        }

        private void UpdatePeriodLabel()
        {
            if (FindName("PeriodLabelText") is not TextBlock label)
                return;

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

        private void ManualDateChanged(object sender, SelectionChangedEventArgs e)
        {
            var startPicker = FindName("StartPicker") as DatePicker;
            var endPicker = FindName("EndPicker") as DatePicker;

            if (startPicker?.SelectedDate == null || endPicker?.SelectedDate == null)
                return;

            _startDate = startPicker.SelectedDate.Value.Date;
            _endDate = endPicker.SelectedDate.Value.Date;

            if (_startDate > _endDate)
                (_startDate, _endDate) = (_endDate, _startDate);

            _mode = DateRangeMode.Custom;
            UpdatePeriodLabel();
            LoadCharts();
        }

        // ========================= WYKRESY I TABELKI =========================

        private void LoadCharts()
        {
            DateTime start = _startDate;
            DateTime end = _endDate;

            if (start == default || start == DateTime.MinValue) start = DateTime.Today;
            if (end == default || end == DateTime.MinValue) end = DateTime.Today;
            if (start > end) (start, end) = (end, start);

            var expenses = DatabaseService.GetSpendingByCategorySafe(_uid, start, end);
            BuildPie(PieCurrent, expenses);

            var incomes = DatabaseService.GetIncomeBySourceSafe(_uid, start, end);
            BuildPie(PieIncome, incomes);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                BindExpenseTable(expenses);
                BindIncomeTable(incomes);
                BindMerchantBars(start, end);
            }), DispatcherPriority.Loaded);
        }

        private void BindExpenseTable(
            System.Collections.Generic.IEnumerable<DatabaseService.CategoryAmountDto> data)
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

        private void BindIncomeTable(
            System.Collections.Generic.IEnumerable<DatabaseService.CategoryAmountDto> data)
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

        private void BindMerchantBars(DateTime start, DateTime end)
        {
            if (FindName("TopMerchantBars") is not ItemsControl shopBars)
                return;

            try
            {
                var shops = DatabaseService.GetSpendingByMerchantSafe(_uid, start, end);
                var sum = shops.Sum(x => x.Amount);

                var rows = shops
                    .OrderByDescending(x => x.Amount)
                    .Take(5)
                    .Select(x => new TableRow
                    {
                        Name = x.Name,
                        Amount = x.Amount,
                        Percent = sum > 0 ? (double)(x.Amount / sum) * 100.0 : 0.0
                    })
                    .ToList();

                shopBars.ItemsSource = rows;
            }
            catch
            {
                shopBars.ItemsSource = Array.Empty<TableRow>();
            }
        }

        private void BuildPie(
            ObservableCollection<PieSlice> target,
            System.Collections.Generic.IEnumerable<DatabaseService.CategoryAmountDto> source)
        {
            target.Clear();

            var data = (source ?? Enumerable.Empty<DatabaseService.CategoryAmountDto>())
                .Where(x => x.Amount > 0m)
                .OrderByDescending(x => x.Amount)
                .ToList();
            if (data.Count == 0) return;

            var sum = data.Sum(x => x.Amount);
            double start = 0;
            int i = 0;

            foreach (var item in data)
            {
                var sweep = (double)(item.Amount / sum) * 360.0;
                if (sweep <= 0) continue;

                var slice = PieSlice.Create(
                    centerX: 180, centerY: 180, radius: 170,
                    startAngle: start, sweepAngle: sweep,
                    brush: DefaultBrush(i++),
                    name: item.Name, amount: item.Amount);

                target.Add(slice);
                start += sweep;
            }
        }

        private Brush DefaultBrush(int i)
        {
            Color[] palette =
            {
                Hex("#FFED7A1A"), // brand orange
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

    // ========================= MODELE POMOCNICZE =========================

    public sealed class PieSlice
    {
        public string Name { get; init; } = "";
        public decimal Amount { get; init; }
        public Brush Brush { get; init; } = Brushes.Gray;
        public Geometry Geometry { get; init; } = Geometry.Empty;

        public static PieSlice Create(double centerX, double centerY, double radius,
                                      double startAngle, double sweepAngle,
                                      Brush brush, string name, decimal amount)
        {
            if (sweepAngle <= 0) sweepAngle = 0.1;
            if (sweepAngle >= 360) sweepAngle = 359.999;

            double a0 = DegToRad(startAngle - 90);
            double a1 = DegToRad(startAngle + sweepAngle - 90);

            Point p0 = new(centerX + radius * Math.Cos(a0), centerY + radius * Math.Sin(a0));
            Point p1 = new(centerX + radius * Math.Cos(a1), centerY + radius * Math.Sin(a1));
            bool large = sweepAngle > 180;

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(centerX, centerY), true, true);
                ctx.LineTo(p0, true, true);
                ctx.ArcTo(p1, new Size(radius, radius), 0, large,
                          SweepDirection.Clockwise, true, true);
            }
            g.Freeze();

            return new PieSlice { Name = name, Amount = amount, Brush = brush, Geometry = g };
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
}

























