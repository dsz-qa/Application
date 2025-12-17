using System.Windows.Controls;
using Finly.ViewModels;
using System.ComponentModel;
using System.Windows;
using Finly.Views.Controls; // add namespace for custom controls and event args
using Finly.Services;
using Finly.Helpers;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        private readonly int _userId;

        public ReportsPage()
        {
            InitializeComponent();

            // DataContext jest ustawiony w XAML, ale na wszelki wypadek zostawiamy fallback
            if (DataContext == null)
                DataContext = new ReportsViewModel();

            // Podpinamy zachowanie paska okresu i reagowanie na zmiany w ViewModelu
            Loaded += (_, __) =>
            {
                if (DataContext is ReportsViewModel vm && PeriodBar != null)
                {
                    // Przy zmianie zakresu / kliknięciu "Szukaj" – odśwież raporty
                    PeriodBar.SearchClicked += (s, e) =>
                    {
                        if (vm.RefreshCommand.CanExecute(null))
                            vm.RefreshCommand.Execute(null);
                    };

                    PeriodBar.RangeChanged += (s, e) =>
                    {
                        if (vm.RefreshCommand.CanExecute(null))
                            vm.RefreshCommand.Execute(null);
                    };

                    // Wyczyść filtry + odśwież
                    PeriodBar.ClearClicked += (s, e) =>
                    {
                        vm.ResetFilters();
                        if (vm.RefreshCommand.CanExecute(null))
                            vm.RefreshCommand.Execute(null);
                    };

                    vm.PropertyChanged += Vm_PropertyChanged;

                    // Pierwsze odświeżenie po załadowaniu strony
                    if (vm.RefreshCommand.CanExecute(null))
                        vm.RefreshCommand.Execute(null);
                }
            };

            Unloaded += (_, __) =>
            {
                if (DataContext is ReportsViewModel vm)
                {
                    vm.PropertyChanged -= Vm_PropertyChanged;
                }
            };
        }

        public ReportsPage(int userId) : this()
        {
            _userId = userId;
            // W przyszłości możesz przekazywać userId do VM, jeśli będzie potrzeba.
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ReportsViewModel vm)
            {
                if (e.PropertyName == nameof(vm.ChartTotals) || e.PropertyName == nameof(vm.ChartTotalAll))
                {
                    // Rysowanie wykresu donut na podstawie słownika ChartTotals
                    try
                    {
                        var dict = vm.ChartTotals ?? new System.Collections.Generic.Dictionary<string, decimal>();
                        var brushes = new System.Windows.Media.Brush[]
                        {
                            (System.Windows.Media.Brush)FindResource("Accent"),
                            System.Windows.Media.Brushes.MediumSeaGreen,
                            System.Windows.Media.Brushes.Orange,
                            System.Windows.Media.Brushes.CadetBlue,
                            System.Windows.Media.Brushes.MediumPurple
                        };

                        MainChart.Draw(dict, vm.ChartTotalAll, brushes);
                        MainChart.SliceClicked -= MainChart_SliceClicked;
                        MainChart.SliceClicked += MainChart_SliceClicked;
                    }
                    catch
                    {
                        // cicho – nie chcemy wysypywać się na braku zasobów itp.
                    }
                }
            }
        }

        private void MainChart_SliceClicked(object? sender, SliceClickedEventArgs e)
        {
            if (DataContext is ReportsViewModel vm)
            {
                // opis w środku dona zamiast drilldownu
                vm.UpdateSelectedSliceInfo(e.Name);
                // jeśli chcesz także drilldown w tabeli:
                // vm.ShowDrilldown(e.Name);
            }
        }

        // BackButton w XAML ma Command={Binding BackCommand}, więc ten handler nie jest już używany,
        // ale zostawiamy go na wypadek gdybyś kiedyś podpięła Click w XAML.
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ReportsViewModel vm)
                vm.BackToSummary();
        }


        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ReportsViewModel vm)
                return;

            try
            {
                const int w = 900;
                const int h = 600; // kluczowe: większa wysokość, żeby donut się zmieścił


                MainChart.Measure(new Size(w, h));
                MainChart.Arrange(new Rect(0, 0, w, h));
                MainChart.UpdateLayout();

                var chartPng = UiRenderHelper.RenderToPng(MainChart, w, h, dpi: 192);

                var path = PdfExportService.ExportReportsPdf(vm, chartPng);
                ToastService.Success($"Raport PDF zapisano na pulpicie: {path}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"Błąd eksportu PDF: {ex.Message}");
            }
        }



    }
}
