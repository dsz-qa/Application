using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Finly.ViewModels;

using LiveChartsCore.SkiaSharpView.WPF;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Finly.Pages
{
    public partial class ChartsPage : UserControl
    {
        private readonly ChartsViewModel _vm;
        private bool _firstLayoutFixDone;

        // ===== Kontrolki pobierane przez FindName (bez zależności od konkretnych klas kontrolek) =====
        private FrameworkElement? _periodBar;

        private Button? _modeAllBtn;
        private Button? _modeExpensesBtn;
        private Button? _modeIncomesBtn;
        private Button? _modeTransferBtn;

        private FrameworkElement? _categoriesDonut;
        private TextBlock? _transferCategoryHint;

        private CartesianChart? _trendChart;
        private CartesianChart? _freeCashChart;
        private CartesianChart? _savedCashChart;
        private CartesianChart? _bankAccountsChart;
        private CartesianChart? _envelopesChart;
        private CartesianChart? _weekdayChart;
        private CartesianChart? _amountBucketsChart;

        private static readonly Brush[] DonutPalette =
        {
            NewFrozenBrush(0xFF,0x98,0x00),
            NewFrozenBrush(0x21,0x96,0xF3),
            NewFrozenBrush(0x9C,0x27,0xB0),
            NewFrozenBrush(0x4C,0xAF,0x50),
            NewFrozenBrush(0xF4,0x43,0x36),
            NewFrozenBrush(0x00,0x96,0x88),
            NewFrozenBrush(0xFF,0xC1,0x07),
            NewFrozenBrush(0x60,0x7D,0x8B),
        };

        private static Brush NewFrozenBrush(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public ChartsPage()
        {
            InitializeComponent();

            _vm = new ChartsViewModel();
            DataContext = _vm;

            Loaded += ChartsPage_Loaded;
            Unloaded += ChartsPage_Unloaded;
        }

        private void ChartsPage_Loaded(object sender, RoutedEventArgs e)
        {
            ResolveNamedControls();

            HookPeriodBar_Reflection();
            ApplySeriesLabelPaint();

            // DOMYŚLNIE: Wszystkie transakcje
            if (_modeAllBtn != null)
                SetActiveMode(_modeAllBtn);

            _vm.SetMode("All");

            AttachFirstRenderFix();

            RefreshCategoriesDonutDeferred();
        }

        private void ChartsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            UnhookPeriodBar_Reflection();
            UnhookChartsSizeChanged();
        }

        // ===== Pobranie kontrolek po nazwach =====

        private void ResolveNamedControls()
        {
            _periodBar = FindName("PeriodBar") as FrameworkElement;

            _modeAllBtn = FindName("ModeAllBtn") as Button;
            _modeExpensesBtn = FindName("ModeExpensesBtn") as Button;
            _modeIncomesBtn = FindName("ModeIncomesBtn") as Button;
            _modeTransferBtn = FindName("ModeTransferBtn") as Button;

            _categoriesDonut = FindName("CategoriesDonut") as FrameworkElement;
            _transferCategoryHint = FindName("TransferCategoryHint") as TextBlock;

            _trendChart = FindName("TrendChart") as CartesianChart;
            _freeCashChart = FindName("FreeCashChart") as CartesianChart;
            _savedCashChart = FindName("SavedCashChart") as CartesianChart;
            _bankAccountsChart = FindName("BankAccountsChart") as CartesianChart;
            _envelopesChart = FindName("EnvelopesChart") as CartesianChart;
            _weekdayChart = FindName("WeekdayChart") as CartesianChart;
            _amountBucketsChart = FindName("AmountBucketsChart") as CartesianChart;
        }

        // ===== LiveCharts: pierwszy render / wymuszenie redraw =====

        private void AttachFirstRenderFix()
        {
            // Po zakończeniu layoutu
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ForceChartsRedraw();
                _firstLayoutFixDone = true;
            }), DispatcherPriority.Loaded);

            HookChartsSizeChanged();
        }

        private void HookChartsSizeChanged()
        {
            if (_trendChart != null) _trendChart.SizeChanged += AnyChart_SizeChanged;
            if (_freeCashChart != null) _freeCashChart.SizeChanged += AnyChart_SizeChanged;
            if (_savedCashChart != null) _savedCashChart.SizeChanged += AnyChart_SizeChanged;
            if (_bankAccountsChart != null) _bankAccountsChart.SizeChanged += AnyChart_SizeChanged;
            if (_envelopesChart != null) _envelopesChart.SizeChanged += AnyChart_SizeChanged;
            if (_weekdayChart != null) _weekdayChart.SizeChanged += AnyChart_SizeChanged;
            if (_amountBucketsChart != null) _amountBucketsChart.SizeChanged += AnyChart_SizeChanged;
        }

        private void UnhookChartsSizeChanged()
        {
            if (_trendChart != null) _trendChart.SizeChanged -= AnyChart_SizeChanged;
            if (_freeCashChart != null) _freeCashChart.SizeChanged -= AnyChart_SizeChanged;
            if (_savedCashChart != null) _savedCashChart.SizeChanged -= AnyChart_SizeChanged;
            if (_bankAccountsChart != null) _bankAccountsChart.SizeChanged -= AnyChart_SizeChanged;
            if (_envelopesChart != null) _envelopesChart.SizeChanged -= AnyChart_SizeChanged;
            if (_weekdayChart != null) _weekdayChart.SizeChanged -= AnyChart_SizeChanged;
            if (_amountBucketsChart != null) _amountBucketsChart.SizeChanged -= AnyChart_SizeChanged;
        }

        private void AnyChart_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_firstLayoutFixDone) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ForceChartsRedraw();
                _firstLayoutFixDone = true;
            }), DispatcherPriority.Background);
        }

        private void ForceChartsRedraw()
        {
            // W LiveChartsCore WPF nie zawsze istnieje Update(), ale zawsze jest WPF-owe Invalidate/UpdateLayout
            TryRedraw(_trendChart);
            TryRedraw(_freeCashChart);
            TryRedraw(_savedCashChart);
            TryRedraw(_bankAccountsChart);
            TryRedraw(_envelopesChart);
            TryRedraw(_weekdayChart);
            TryRedraw(_amountBucketsChart);
        }

        private static void TryRedraw(FrameworkElement? fe)
        {
            if (fe == null) return;
            try { fe.InvalidateVisual(); } catch { }
            try { fe.UpdateLayout(); } catch { }
        }

        // ===== PeriodBar (REFLECTION: bez zależności od klasy kontrolki) =====

        private Delegate? _rangeChangedHandler;
        private Delegate? _searchClickedHandler;

        private void HookPeriodBar_Reflection()
        {
            if (_periodBar == null) return;

            // Zakładamy eventy: RangeChanged, SearchClicked oraz właściwości: StartDate, EndDate
            _rangeChangedHandler = TryHookEvent(_periodBar, "RangeChanged", nameof(PeriodBar_RangeChanged_Reflection));
            _searchClickedHandler = TryHookEvent(_periodBar, "SearchClicked", nameof(PeriodBar_SearchClicked_Reflection));
        }

        private void UnhookPeriodBar_Reflection()
        {
            if (_periodBar == null) return;

            TryUnhookEvent(_periodBar, "RangeChanged", _rangeChangedHandler);
            TryUnhookEvent(_periodBar, "SearchClicked", _searchClickedHandler);

            _rangeChangedHandler = null;
            _searchClickedHandler = null;
        }

        private Delegate? TryHookEvent(object target, string eventName, string localHandlerName)
        {
            try
            {
                var evt = target.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public);
                if (evt == null) return null;

                // Typ delegata eventu
                var handlerType = evt.EventHandlerType;
                if (handlerType == null) return null;

                // Nasza metoda musi pasować sygnaturą: (object?, EventArgs)
                var mi = GetType().GetMethod(localHandlerName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi == null) return null;

                var del = Delegate.CreateDelegate(handlerType, this, mi);
                evt.AddEventHandler(target, del);
                return del;
            }
            catch
            {
                return null;
            }
        }

        private void TryUnhookEvent(object target, string eventName, Delegate? del)
        {
            if (del == null) return;

            try
            {
                var evt = target.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public);
                evt?.RemoveEventHandler(target, del);
            }
            catch
            {
                // ignorujemy
            }
        }

        // Handlery okresu (wywoływane refleksją)
        private void PeriodBar_RangeChanged_Reflection(object? sender, EventArgs e) => ApplyPeriodBarRange();
        private void PeriodBar_SearchClicked_Reflection(object? sender, EventArgs e) => ApplyPeriodBarRange();

        private void ApplyPeriodBarRange()
        {
            if (_periodBar == null) return;

            var start = ReadDateProperty(_periodBar, "StartDate");
            var end = ReadDateProperty(_periodBar, "EndDate");
            if (start == null || end == null) return;

            _vm.SetCustomRange(start.Value, end.Value);
            RefreshCategoriesDonutDeferred();
            Dispatcher.BeginInvoke(new Action(ForceChartsRedraw), DispatcherPriority.Background);
        }

        private static DateTime? ReadDateProperty(object target, string propName)
        {
            try
            {
                var p = target.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                var v = p?.GetValue(target);
                if (v is DateTime dt) return dt;
                if (v is DateTime ndt) return ndt;
                return null;
            }
            catch
            {
                return null;
            }
        }

        // ===== Kosmetyka etykiet =====

        private void ApplySeriesLabelPaint()
        {
            try
            {
                var white = new SolidColorPaint(new SKColor(255, 255, 255));

                foreach (var s in _vm.CategoriesSeries) s.DataLabelsPaint = white;
                foreach (var s in _vm.BankAccountsSeries) s.DataLabelsPaint = white;
                foreach (var s in _vm.TrendSeries) s.DataLabelsPaint = white;
                foreach (var s in _vm.WeekdaySeries) s.DataLabelsPaint = white;
            }
            catch
            {
                // kosmetyka – ignorujemy
            }
        }

        // ===== Tryby =====

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            SetActiveMode(btn);

            var tag = btn.Tag as string;
            if (!string.IsNullOrWhiteSpace(tag))
            {
                _vm.SetMode(tag); // All / Expenses / Incomes / Transfer
                RefreshCategoriesDonutDeferred();
                Dispatcher.BeginInvoke(new Action(ForceChartsRedraw), DispatcherPriority.Background);
            }
        }

        private void SetActiveMode(Button active)
        {
            ResetModeButtons();

            active.BorderThickness = new Thickness(2);

            if (Application.Current.Resources["Brand.Orange"] is Brush orange)
                active.BorderBrush = orange;
            else if (Application.Current.Resources["Brand.Blue"] is Brush blue)
                active.BorderBrush = blue;
        }

        private void ResetModeButtons()
        {
            ClearBorder(_modeAllBtn);
            ClearBorder(_modeExpensesBtn);
            ClearBorder(_modeIncomesBtn);
            ClearBorder(_modeTransferBtn);
        }

        private void ClearBorder(Button? btn)
        {
            if (btn == null) return;

            btn.BorderThickness = new Thickness(1);
            if (Application.Current.Resources["Brand.Blue"] is Brush blue)
                btn.BorderBrush = blue;
        }

        private string GetActiveModeTag()
        {
            var active =
                new[] { _modeAllBtn, _modeExpensesBtn, _modeIncomesBtn, _modeTransferBtn }
                .FirstOrDefault(b => b != null && b.BorderThickness.Left >= 2);

            return (active?.Tag as string) ?? "All";
        }

        // ===== DONUT =====

        private void RefreshCategoriesDonutDeferred()
        {
            Dispatcher.BeginInvoke(new Action(RefreshCategoriesDonut), DispatcherPriority.Background);
        }

        private void RefreshCategoriesDonut()
        {
            if (_categoriesDonut == null) return;

            var mode = GetActiveModeTag();

            if (mode == "Transfer")
            {
                _categoriesDonut.Visibility = Visibility.Collapsed;
                if (_transferCategoryHint != null) _transferCategoryHint.Visibility = Visibility.Visible;
                return;
            }

            _categoriesDonut.Visibility = Visibility.Visible;
            if (_transferCategoryHint != null) _transferCategoryHint.Visibility = Visibility.Collapsed;

            // Ustaw tytuł jeśli kontrolka ma właściwość Title
            TrySetProperty(_categoriesDonut, "Title", mode switch
            {
                "Expenses" => "Wydatki według kategorii",
                "Incomes" => "Przychody według kategorii",
                _ => "Wszystkie transakcje — podział"
            });

            var dict = BuildCategoryTotalsFromVm();
            var total = dict.Values.Sum();

            // Wywołaj Draw(dict,total,palette) jeśli istnieje
            TryInvokeDraw(_categoriesDonut, dict, total, DonutPalette);
        }

        private static void TrySetProperty(object target, string propName, object? value)
        {
            try
            {
                var p = target.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (p != null && p.CanWrite) p.SetValue(target, value);
            }
            catch
            {
                // ignorujemy
            }
        }

        private static void TryInvokeDraw(object donutControl, Dictionary<string, decimal> dict, decimal total, Brush[] palette)
        {
            try
            {
                // Szukamy metody Draw(Dictionary<string,decimal>, decimal, Brush[])
                var mi = donutControl.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m =>
                    {
                        if (!string.Equals(m.Name, "Draw", StringComparison.Ordinal)) return false;
                        var ps = m.GetParameters();
                        if (ps.Length != 3) return false;
                        return ps[0].ParameterType.IsAssignableFrom(typeof(Dictionary<string, decimal>))
                               && ps[1].ParameterType == typeof(decimal)
                               && ps[2].ParameterType.IsAssignableFrom(typeof(Brush[]));
                    });

                mi?.Invoke(donutControl, new object[] { dict, total, palette });
            }
            catch
            {
                // jeśli kontrolka nie ma Draw albo inna sygnatura – ignorujemy
            }
        }

        private Dictionary<string, decimal> BuildCategoryTotalsFromVm()
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in _vm.CategoriesSeries)
            {
                var name = (s?.Name ?? "Inne").Trim();
                if (string.IsNullOrWhiteSpace(name)) name = "Inne";

                decimal sum = 0m;

                // PieSeries<double> zwykle ma Values typu IEnumerable<double>
                object? valuesObj = null;
                try
                {
                    var p = s?.GetType().GetProperty("Values", BindingFlags.Instance | BindingFlags.Public);
                    valuesObj = p?.GetValue(s);
                }
                catch { }

                if (valuesObj is IEnumerable<double> doubles)
                {
                    foreach (var v in doubles) sum += (decimal)v;
                }
                else if (valuesObj is System.Collections.IEnumerable en)
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
