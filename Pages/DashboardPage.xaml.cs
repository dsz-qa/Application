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
using System.Windows.Threading;

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
                try { PeriodBar.SetPreset(DateRangeMode.Day); } catch { /* ignoruj jeśli brak */ }
                RefreshKpis();
                LoadBanks();
                LoadCharts();
            };
        }

        // ========================= KPI / BANKI =========================

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

        // ========================= ZAKRES DAT =========================

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
                LoadCharts();
            }
            catch { /* nic */ }
        }

        // ========================= WYKRESY I TABELKI =========================

        private void LoadCharts()
        {
            // StartDate/EndDate są DateTime (nie-nullable)
            DateTime start = PeriodBar.StartDate;
            DateTime end = PeriodBar.EndDate;

            if (start == default || start == DateTime.MinValue) start = DateTime.Today;
            if (end == default || end == DateTime.MinValue) end = DateTime.Today;
            if (start > end) (start, end) = (end, start);

            // KOŁA
            var expenses = DatabaseService.GetSpendingByCategorySafe(_uid, start, end);
            BuildPie(PieCurrent, expenses);

            var incomes = DatabaseService.GetIncomeBySourceSafe(_uid, start, end);
            BuildPie(PieIncome, incomes);

            // TABELKI + paski po prawej (po wyrenderowaniu drzewa wizualnego)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BindExpenseTable(expenses);
                BindIncomeTable(incomes);
                BindMerchantBars(start, end);   // prawa kolumna: „sklepy”
            }), DispatcherPriority.Loaded);
        }

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

            // prawa kolumna – „Najwyższe wydatki — kategorie”
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
                // bezpieczny fallback
                shopBars.ItemsSource = Array.Empty<TableRow>();
            }
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
                ctx.ArcTo(p1, new Size(radius, radius), 0, large, SweepDirection.Clockwise, true, true);
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
        public double Percent { get; set; }          // 0..100
        public string PercentStr => Math.Round(Percent, 0) + "%";
    }
}


















