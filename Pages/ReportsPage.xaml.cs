using System.Windows.Controls;
using Finly.ViewModels;
using System.ComponentModel;
using System.Windows;

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

                if (PeriodBar != null)
                {
                    // Mamy tu anonimowe delegaty, więc realnie ich nie odepniesz,
                    // ale zostawiamy komentarz – w większej aplikacji warto to zrobić nazwanymi handlerami.
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

        private void MainChart_SliceClicked(object? sender, Views.Controls.SliceClickedEventArgs e)
        {
            if (DataContext is ReportsViewModel vm)
            {
                vm.ShowDrilldown(e.Name);
            }
        }

        // BackButton w XAML ma Command={Binding BackCommand}, więc ten handler nie jest już używany,
        // ale zostawiamy go na wypadek gdybyś kiedyś podpięła Click w XAML.
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ReportsViewModel vm)
                vm.BackToSummary();
        }
    }
}
