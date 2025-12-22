using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Finly.ViewModels;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Finly.Pages
{
    public partial class ChartsPage : UserControl
    {
        private readonly ChartsViewModel _vm;

        // Prosta paleta do donuta (możesz ją podmienić na swoje zasoby)
        private static readonly Brush[] DonutPalette = new Brush[]
        {
            new SolidColorBrush(Color.FromRgb(0xFF,0x98,0x00)), // orange
            new SolidColorBrush(Color.FromRgb(0x21,0x96,0xF3)), // blue
            new SolidColorBrush(Color.FromRgb(0x9C,0x27,0xB0)), // purple
            new SolidColorBrush(Color.FromRgb(0x4C,0xAF,0x50)), // green
            new SolidColorBrush(Color.FromRgb(0xF4,0x43,0x36)), // red
            new SolidColorBrush(Color.FromRgb(0x00,0x96,0x88)), // teal
            new SolidColorBrush(Color.FromRgb(0xFF,0xC1,0x07)), // amber
            new SolidColorBrush(Color.FromRgb(0x60,0x7D,0x8B)), // blue grey
        };

        public ChartsPage()
        {
            InitializeComponent();

            _vm = new ChartsViewModel();
            DataContext = _vm;

            Loaded += ChartsPage_Loaded;
        }

        private void ChartsPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ChartsPage_Loaded;

            HookPeriodBar();
            ApplySeriesLabelPaint();

            // DOMYŚLNIE: Wszystkie transakcje
            if (ModeAllBtn != null)
            {
                SetActiveMode(ModeAllBtn);
                _vm.SetMode("All");
            }

            RefreshCategoriesDonutDeferred();
        }

        private void HookPeriodBar()
        {
            if (PeriodBar == null) return;

            PeriodBar.RangeChanged += (_, __) =>
            {
                _vm.SetCustomRange(PeriodBar.StartDate, PeriodBar.EndDate);
                RefreshCategoriesDonutDeferred();
            };

            PeriodBar.SearchClicked += (_, __) =>
            {
                _vm.SetCustomRange(PeriodBar.StartDate, PeriodBar.EndDate);
                RefreshCategoriesDonutDeferred();
            };
        }

        private void ApplySeriesLabelPaint()
        {
            try
            {
                var white = new SolidColorPaint(new SKColor(255, 255, 255));

                if (_vm.CategoriesSeries != null)
                    foreach (var s in _vm.CategoriesSeries)
                        s.DataLabelsPaint = white;

                if (_vm.BankAccountsSeries != null)
                    foreach (var s in _vm.BankAccountsSeries)
                        s.DataLabelsPaint = white;

                if (_vm.TrendSeries != null)
                    foreach (var s in _vm.TrendSeries)
                        s.DataLabelsPaint = white;

                if (_vm.WeekdaySeries != null)
                    foreach (var s in _vm.WeekdaySeries)
                        s.DataLabelsPaint = white;
            }
            catch
            {
                // kosmetyka – ignorujemy
            }
        }

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            SetActiveMode(btn);

            var tag = btn.Tag as string;
            if (!string.IsNullOrWhiteSpace(tag))
            {
                _vm.SetMode(tag); // All / Expenses / Incomes / Transfer / Cashflow
                RefreshCategoriesDonutDeferred();
            }
        }

        private void SetActiveMode(Button active)
        {
            ResetModeButtons();

            active.BorderThickness = new Thickness(2);
            if (Application.Current.Resources["Brand.Orange"] is Brush orange)
                active.BorderBrush = orange;
        }

        private void ResetModeButtons()
        {
            ClearBorder(ModeAllBtn);
            ClearBorder(ModeExpensesBtn);
            ClearBorder(ModeIncomesBtn);
            ClearBorder(ModeTransferBtn);
            ClearBorder(ModeCashflowBtn);
        }

        private void ClearBorder(Button? btn)
        {
            if (btn == null) return;

            btn.BorderThickness = new Thickness(1);
            if (Application.Current.Resources["Brand.Blue"] is Brush blue)
                btn.BorderBrush = blue;
        }

        // ===== DONUT: odświeżanie danych =====

        private void RefreshCategoriesDonutDeferred()
        {
            Dispatcher.BeginInvoke(new Action(RefreshCategoriesDonut), DispatcherPriority.Background);
        }

        private void RefreshCategoriesDonut()
        {
            if (CategoriesDonut == null) return;

            var mode = GetActiveModeTag();

            CategoriesDonut.Title = mode switch
            {
                "Expenses" => "Wydatki według kategorii",
                "Incomes" => "Przychody według kategorii",
                "Transfer" => "Transfery",
                "Cashflow" => "Cashflow według kategorii",
                _ => "Wszystkie transakcje — podział"
            };

            // _vm.CategoriesSeries to PieSeries<double> (LiveCharts).
            // My robimy z tego: Dictionary<string, decimal> i total
            var dict = BuildCategoryTotalsFromVm();
            var total = dict.Values.Sum();

            CategoriesDonut.Draw(dict, total, DonutPalette);
        }

        private Dictionary<string, decimal> BuildCategoryTotalsFromVm()
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in _vm.CategoriesSeries)
            {
                // PieSeries<double> ma Name oraz Values (IEnumerable<double>)
                var name = (s?.Name ?? "Inne").Trim();
                if (string.IsNullOrWhiteSpace(name)) name = "Inne";

                decimal sum = 0m;

                var valuesProp = s?.GetType().GetProperty("Values");
                var valuesObj = valuesProp?.GetValue(s);

                if (valuesObj is System.Collections.IEnumerable en)
                {
                    foreach (var v in en)
                    {
                        if (v == null) continue;
                        if (v is IConvertible conv)
                        {
                            try
                            {
                                var d = conv.ToDouble(CultureInfo.InvariantCulture);
                                sum += (decimal)d;
                            }
                            catch { }
                        }
                    }
                }

                if (sum <= 0m) continue;

                if (result.ContainsKey(name)) result[name] += sum;
                else result[name] = sum;
            }

            return result
                .OrderByDescending(kv => kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        private string GetActiveModeTag()
        {
            Button? active =
                new[] { ModeAllBtn, ModeExpensesBtn, ModeIncomesBtn, ModeTransferBtn, ModeCashflowBtn }
                .FirstOrDefault(b => b != null && b.BorderThickness.Left >= 2);

            return (active?.Tag as string) ?? "All";
        }

        // ===== Eksport =====

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            await _vm.ExportToPdfAsync();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            _vm.ExportToCsv();
        }
    }
}
