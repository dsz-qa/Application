using System.Windows.Controls;
using Finly.ViewModels;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        private readonly int _userId;

        public ReportsPage()
        {
            InitializeComponent();
            // DataContext is set in XAML, but ensure there's a fallback
            if (this.DataContext == null)
                this.DataContext = new ReportsViewModel();

            // Hook up period bar events to trigger viewmodel refresh
            Loaded += (_, __) =>
            {
                if (DataContext is ReportsViewModel vm && PeriodBar != null)
                {
                    PeriodBar.SearchClicked += (s, e) => { if (vm.RefreshCommand.CanExecute(null)) vm.RefreshCommand.Execute(null); };
                    PeriodBar.RangeChanged += (s, e) => { if (vm.RefreshCommand.CanExecute(null)) vm.RefreshCommand.Execute(null); };
                    PeriodBar.ClearClicked += (s, e) => { vm.ResetFilters(); if (vm.RefreshCommand.CanExecute(null)) vm.RefreshCommand.Execute(null); };

                    vm.PropertyChanged += Vm_PropertyChanged;

                    // initial draw
                    vm.RefreshCommand.Execute(null);
                }
            };

            Unloaded += (_, __) =>
            {
                if (PeriodBar != null)
                {
                    // best-effort unsubscribe (anonymous handlers above can't be removed easily),
                    // in larger app keep named handlers to unsubscribe properly.
                }
            };
        }

        public ReportsPage(int userId) : this()
        {
            _userId = userId;
            // Optionally pass userId to ViewModel in future
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ReportsViewModel vm)
            {
                if (e.PropertyName == nameof(vm.ChartTotals) || e.PropertyName == nameof(vm.ChartTotalAll))
                {
                    // Draw chart
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
                    catch { }
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ReportsViewModel vm)
                vm.BackToSummary();
        }
    }
}
