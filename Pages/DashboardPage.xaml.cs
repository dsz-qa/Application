using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl
    {
        private readonly int _uid;

        public ObservableCollection<PieSlice> PieCurrent { get; } = new();
        public ObservableCollection<PieSlice> PieIncome { get; } = new();

        public DashboardPage(int userId)
        {
            InitializeComponent();
            _uid = userId;
            DataContext = this;

            Loaded += (_, __) =>
            {
                // jeśli PeriodBar ma preset – ustaw Dzisiaj; w razie czego ignoruj
                try { PeriodBar.SetPreset(DateRangeMode.Day); } catch { }

                RefreshKpis();
                LoadBanks();
                LoadCharts();
            };
        }

        private void RefreshKpis()
        {
            var s = DatabaseService.GetMoneySnapshot(_uid);
            var freeCash = Math.Max(0m, s.Cash - s.Envelopes);
            var total = s.Banks + freeCash + s.Envelopes;

            KpiTotal.Text = total.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            KpiFreeCash.Text = freeCash.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            KpiEnvelopes.Text = s.Envelopes.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            BanksExpander.Header = s.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void LoadBanks()
        {
            var list = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();
            BanksList.ItemsSource = list.Select(a => new { a.AccountName, a.Balance }).ToList();
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            RefreshKpis();
            LoadBanks();
            LoadCharts();
        }

        private void ManualDateChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (PeriodBar.Mode != DateRangeMode.Custom)
                    PeriodBar.Mode = DateRangeMode.Custom;
            }
            catch { }
        }

        // ===== WYKRESY =====
        private void LoadCharts()
        {
            // U Ciebie StartDate/EndDate są DateTime (nie-nullable),
            // więc NIE używamy '??'. Zabezpieczenie przez '== default'.
            DateTime start = PeriodBar.StartDate;
            DateTime end = PeriodBar.EndDate;

            if (start == default) start = DateTime.Today;
            if (end == default) end = DateTime.Today;
            if (start > end) { var t = start; start = end; end = t; }

            // Wydatki
            var expenses = DatabaseService.GetSpendingByCategorySafe(_uid, start, end);
            BuildPie(PieCurrent, expenses);
            if (NoDataPieCurrent != null)
                NoDataPieCurrent.Visibility = PieCurrent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Przychody
            var incomes = DatabaseService.GetIncomeBySourceSafe(_uid, start, end);
            BuildPie(PieIncome, incomes);
            if (NoDataPieIncome != null)
                NoDataPieIncome.Visibility = PieIncome.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildPie(
            ObservableCollection<PieSlice> target,
            IEnumerable<DatabaseService.CategoryAmountDto> source)
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
            Color[] palette = new[]
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
                ctx.ArcTo(p1, new Size(radius, radius), 0, large, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();

            return new PieSlice { Name = name, Amount = amount, Brush = brush, Geometry = g };
        }

        private static double DegToRad(double deg) => Math.PI / 180 * deg;
    }
}
















