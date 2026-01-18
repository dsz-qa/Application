using System;
using System.Windows;
using System.Windows.Controls;
using Finly.Services;
using Finly.ViewModels;
using Finly.Views.Controls;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        // zabezpieczenie przed wielokrotnym podpinaniem eventów (WPF potrafi ponownie wywołać Loaded)
        private bool _eventsHooked;

        public ReportsPage()
        {
            InitializeComponent();

            Loaded += ReportsPage_Loaded;
            Unloaded += ReportsPage_Unloaded;
        }

        // jeśli gdzieś tworzysz ReportsPage(int userId), zostawiamy, ale nic nie robimy,
        // bo VM i tak bierze userId z UserService
        public ReportsPage(int userId) : this() { }

        private void ReportsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_eventsHooked) return;
            _eventsHooked = true;

            try
            {
                if (DataContext is not ReportsViewModel vm)
                    return;

                if (PeriodBar != null)
                {
                    PeriodBar.SearchClicked += PeriodBar_SearchClicked;
                    PeriodBar.RangeChanged += PeriodBar_RangeChanged;
                    PeriodBar.ClearClicked += PeriodBar_ClearClicked;
                }

                // Pierwsze odświeżenie po załadowaniu strony
                SafeExecute(vm.RefreshCommand);
            }
            catch
            {
                // celowo bez MessageBox (nie blokujemy UI)
            }
        }

        private void ReportsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_eventsHooked) return;
            _eventsHooked = false;

            try
            {
                if (PeriodBar != null)
                {
                    PeriodBar.SearchClicked -= PeriodBar_SearchClicked;
                    PeriodBar.RangeChanged -= PeriodBar_RangeChanged;
                    PeriodBar.ClearClicked -= PeriodBar_ClearClicked;
                }
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

                SafeExecute(vm.RefreshCommand);
            }
            catch { }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ReportsViewModel vm)
                return;

            try
            {
                // Najbezpieczniej: bez dodatkowych kontrolek renderujących.
                var path = PdfExportService.ExportReportsPdf(vm, null);
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
