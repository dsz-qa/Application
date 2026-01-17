using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Finly.Helpers;
using Finly.Services;
using Finly.ViewModels;
using Finly.Views.Controls;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        private readonly int _userId;

        // zabezpieczenie przed wielokrotnym podpinaniem eventów (WPF potrafi ponownie wywołać Loaded)
        private bool _eventsHooked;

        public ReportsPage()
        {
            InitializeComponent();
            DataContext = new ReportsViewModel();

            Loaded += ReportsPage_Loaded;
            Unloaded += ReportsPage_Unloaded;
        }

        public ReportsPage(int userId) : this()
        {
            _userId = userId;
        }

        private void ReportsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // kluczowe: nie podpinamy drugi raz
            if (_eventsHooked) return;
            _eventsHooked = true;

            try
            {
                if (DataContext is not ReportsViewModel vm)
                    return;

                // PeriodBar może być null, jeśli XAML się nie zbudował lub kontrolka nie istnieje
                if (PeriodBar != null)
                {
                    PeriodBar.SearchClicked += PeriodBar_SearchClicked;
                    PeriodBar.RangeChanged += PeriodBar_RangeChanged;
                    PeriodBar.ClearClicked += PeriodBar_ClearClicked;
                }

                vm.PropertyChanged += Vm_PropertyChanged;

                // Pierwsze odświeżenie po załadowaniu strony
                SafeExecute(vm.RefreshCommand);
            }
            catch
            {
                // celowo: brak MessageBox – nie chcemy blokować UI w razie problemu w czasie ładowania
            }
        }

        private void ReportsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_eventsHooked) return;
            _eventsHooked = false;

            try
            {
                if (DataContext is ReportsViewModel vm)
                    vm.PropertyChanged -= Vm_PropertyChanged;

                if (PeriodBar != null)
                {
                    PeriodBar.SearchClicked -= PeriodBar_SearchClicked;
                    PeriodBar.RangeChanged -= PeriodBar_RangeChanged;
                    PeriodBar.ClearClicked -= PeriodBar_ClearClicked;
                }

                // MainChart może być null (np. XAML nie zdążył w pełni wejść)
                if (MainChart != null)
                    MainChart.SliceClicked -= MainChart_SliceClicked;
            }
            catch
            {
                // bez wyjątków przy odpinaniu
            }
        }

        private void PeriodBar_SearchClicked(object? sender, EventArgs e)
        {
            try
            {
                if (DataContext is ReportsViewModel vm)
                    SafeExecute(vm.RefreshCommand);
            }
            catch { }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            try
            {
                if (DataContext is ReportsViewModel vm)
                    SafeExecute(vm.RefreshCommand);
            }
            catch { }
        }

        private void PeriodBar_ClearClicked(object? sender, EventArgs e)
        {
            try
            {
                if (DataContext is not ReportsViewModel vm)
                    return;

                vm.ResetFilters();
                SafeExecute(vm.RefreshCommand);
            }
            catch { }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (sender is not ReportsViewModel vm)
                    return;

                // UWAGA: MainChart może być null
                if (MainChart == null)
                    return;

                if (e.PropertyName != nameof(vm.ChartTotals) &&
                    e.PropertyName != nameof(vm.ChartTotalAll))
                    return;

                var dict = vm.ChartTotals ?? new System.Collections.Generic.Dictionary<string, decimal>();

                // Brush "Accent" może nie istnieć (FindResource rzuca wyjątek). Używamy TryFindResource.
                var accent = TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;

                var brushes = new Brush[]
                {
                    accent,
                    Brushes.MediumSeaGreen,
                    Brushes.Orange,
                    Brushes.CadetBlue,
                    Brushes.MediumPurple
                };

                // Draw może rzucić wyjątek np. przy pustych danych/niegotowym layout
                MainChart.Draw(dict, vm.ChartTotalAll, brushes);

                // podpinamy handler raz (odpinamy i podpinamy, ale bez wyjątku)
                MainChart.SliceClicked -= MainChart_SliceClicked;
                MainChart.SliceClicked += MainChart_SliceClicked;
            }
            catch
            {
                // zero wyjątków z VM PropertyChanged, bo inaczej UI może się wysypać
            }
        }

        private void MainChart_SliceClicked(object? sender, SliceClickedEventArgs e)
        {
            try
            {
                if (DataContext is ReportsViewModel vm)
                    vm.UpdateSelectedSliceInfo(e?.Name ?? string.Empty);
            }
            catch { }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ReportsViewModel vm)
                return;

            try
            {
                var accent = TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;

                var brushes = new Brush[]
                {
                    accent,
                    Brushes.MediumSeaGreen,
                    Brushes.Orange,
                    Brushes.CadetBlue,
                    Brushes.MediumPurple
                };

                const int w = 900;
                const int h = 360;

                var export = new ReportDonutWithLegendExportControl
                {
                    Width = w,
                    Height = h
                };

                // rozkład aby kontrolka miała realne wymiary
                export.Measure(new Size(w, h));
                export.Arrange(new Rect(0, 0, w, h));
                export.UpdateLayout();

                export.Build(vm, brushes, maxItems: 9);

                export.UpdateLayout();

                var png = UiRenderHelper.RenderToPng(export, w, h, dpi: 192);

                var path = PdfExportService.ExportReportsPdf(vm, png);
                ToastService.Success($"Raport PDF zapisano na pulpicie: {path}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"Błąd eksportu PDF: {ex.Message}");
            }
        }

        private static void SafeExecute(System.Windows.Input.ICommand? command)
        {
            try
            {
                if (command != null && command.CanExecute(null))
                    command.Execute(null);
            }
            catch
            {
                // nie propagujemy wyjątków z komend
            }
        }
    }
}
