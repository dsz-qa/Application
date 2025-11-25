using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Finly.Services;
using Finly.Views.Controls;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        private readonly int _userId;
        private readonly Brush[] _sliceBrushes = new[]
        {
            (Brush)new SolidColorBrush(Color.FromRgb(0x42,0xA5,0xF5)),
            new SolidColorBrush(Color.FromRgb(0x66,0xBB,0x6A)),
            new SolidColorBrush(Color.FromRgb(0xFF,0xCA,0x28)),
            new SolidColorBrush(Color.FromRgb(0x9E,0x9E,0x9E))
        };

        public ReportsPage()
        {
            InitializeComponent();
            this.Loaded += ReportsPage_Loaded;
            this.SizeChanged += ReportsPage_SizeChanged;
        }

        public ReportsPage(int userId) : this() => _userId = userId;

        private void ReportsPage_Loaded(object? sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var start = new DateTime(today.Year, today.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            if (!FromDatePicker.SelectedDate.HasValue) FromDatePicker.SelectedDate = start;
            if (!ToDatePicker.SelectedDate.HasValue) ToDatePicker.SelectedDate = end;

            RefreshReports();
        }

        private void ReportsPage_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (PieCanvas != null)
                RedrawLastPie();
        }

        private Dictionary<string, decimal>? _lastTotals;
        private decimal _lastTotalAll;

        private void ThisMonthBtn_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var start = new DateTime(today.Year, today.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            FromDatePicker.SelectedDate = start;
            ToDatePicker.SelectedDate = end;
            RefreshReports();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshReports();

        private void RefreshReports()
        {
            int userId = _userId > 0 ? _userId : UserService.GetCurrentUserId();
            if (userId <= 0) return;

            var from = FromDatePicker.SelectedDate ?? DateTime.Today;
            var to = ToDatePicker.SelectedDate ?? DateTime.Today;
            if (to < from) (from, to) = (to, from);

            DataTable? exp = null;
            try { exp = DatabaseService.GetExpenses(userId, from, to); }
            catch { exp = null; }

            decimal totalAll = 0m;
            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Rozrywka"] = 0m,
                ["Subskrypcje"] = 0m,
                ["Jedzenie"] = 0m,
                ["Pozostałe"] = 0m
            };

            if (exp != null)
            {
                foreach (DataRow r in exp.Rows)
                {
                    decimal amt = 0m;
                    try { amt = Math.Abs(Convert.ToDecimal(r["Amount"])); } catch { amt = 0m; }
                    totalAll += amt;

                    string catName = "(brak)";
                    if (exp.Columns.Contains("CategoryName"))
                        catName = r["CategoryName"]?.ToString() ?? "(brak)";
                    else
                        catName = GetCategoryNameByIdSafe(userId, r);

                    var cn = (catName ?? "").Trim().ToLowerInvariant();

                    if (cn.Contains("rozryw") || cn.Contains("entertain") || cn.Contains("kino") || cn.Contains("game") || cn.Contains("gry"))
                        totals["Rozrywka"] += amt;
                    else if (cn.Contains("subskr") || cn.Contains("abon") || cn.Contains("netflix") || cn.Contains("spotify") || cn.Contains("subscription"))
                        totals["Subskrypcje"] += amt;
                    else if (cn.Contains("jedzen") || cn.Contains("jedzenie") || cn.Contains("restaur") || cn.Contains("food") || cn.Contains("jedz"))
                        totals["Jedzenie"] += amt;
                    else
                        totals["Pozostałe"] += amt;
                }
            }

            _lastTotals = totals;
            _lastTotalAll = totalAll;

            var rows = new List<object>();
            foreach (var kv in totals)
            {
                var amount = kv.Value;
                var share = totalAll > 0 ? (double)(amount / totalAll * 100m) : 0.0;
                rows.Add(new { Category = kv.Key, Amount = amount, SharePercent = share });
            }
            rows.Add(new { Category = "Suma wydatków", Amount = totalAll, SharePercent = 100.0 });

            ReportGrid.ItemsSource = rows;

            BuildLegend(totals, totalAll);

            try
            {
                var snap = DatabaseService.GetMoneySnapshot(userId);
                if (snap != null)
                {
                    decimal saved = 0m;
                    try { saved = snap.SavedUnallocated; } catch { }
                    SavedAmountText.Text = saved.ToString("N2", CultureInfo.CurrentCulture) + " zł";
                }
                else
                {
                    SavedAmountText.Text = "—";
                }
            }
            catch
            {
                SavedAmountText.Text = "—";
            }

            DrawPieChart(totals, totalAll);
        }

        private void BuildLegend(Dictionary<string, decimal> totals, decimal totalAll)
        {
            LegendStack.Children.Clear();
            int idx = 0;
            foreach (var kv in totals)
            {
                var color = _sliceBrushes[idx % _sliceBrushes.Length];
                var pct = totalAll > 0 ? (double)(kv.Value / totalAll * 100m) : 0.0;

                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                var rect = new System.Windows.Shapes.Rectangle { Width = 16, Height = 16, Fill = color, Margin = new Thickness(0, 0, 8, 0) };
                var tb = new TextBlock { Text = $"{kv.Key}: {kv.Value:N2} zł ({pct:N1}%)", VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(rect);
                sp.Children.Add(tb);
                LegendStack.Children.Add(sp);

                idx++;
            }
        }

        private void DrawPieChart(Dictionary<string, decimal> totals, decimal totalAll)
        {
            // use DonutChartControl to draw
            PieCanvas.Draw(totals, totalAll, _sliceBrushes);
        }

        private void RedrawLastPie()
        {
            if (_lastTotals != null)
                DrawPieChart(_lastTotals, _lastTotalAll);
        }

        private static string GetCategoryNameByIdSafe(int userId, DataRow row)
        {
            try
            {
                if (row.Table.Columns.Contains("CategoryId") && row["CategoryId"] != DBNull.Value)
                {
                    int id = Convert.ToInt32(row["CategoryId"]);
                    var cats = DatabaseService.GetCategories(userId);
                    if (cats != null)
                    {
                        foreach (DataRow cr in cats.Rows)
                        {
                            try
                            {
                                if (Convert.ToInt32(cr["Id"]) == id)
                                    return cr["Name"]?.ToString() ?? "(brak)";
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return "(brak)";
        }
    }
}
