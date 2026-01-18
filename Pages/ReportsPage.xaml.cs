using Finly.Services;
using Finly.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        // zabezpieczenie przed wielokrotnym podpinaniem eventów (WPF potrafi ponownie wywołać Loaded)
        private bool _eventsHooked;

        private DispatcherTimer? _refreshDebounceTimer;
        private ReportsViewModel? _vmForRefresh;

        private ReportsViewModel? _hookedVm;

        public ReportsPage()
        {
            InitializeComponent();

            Loaded += ReportsPage_Loaded;
            Unloaded += ReportsPage_Unloaded;
            DataContextChanged += ReportsPage_DataContextChanged;
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
                // KLUCZ: jeśli nawigacja/shell ustawia inny DataContext, KPI binduje w próżnię
                if (DataContext is not ReportsViewModel)
                    DataContext = new ReportsViewModel();

                EnsureDebounceTimer();

                // PeriodBar events
                if (PeriodBar != null)
                {
                    PeriodBar.SearchClicked += PeriodBar_SearchClicked;
                    PeriodBar.RangeChanged += PeriodBar_RangeChanged;
                    PeriodBar.ClearClicked += PeriodBar_ClearClicked;
                }

                HookVmPropertyChanged(GetVm());

                // startowy refresh
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RequestRefresh(GetVm());
                }), DispatcherPriority.Background);
            }
            catch
            {
                // celowo cisza (UI ma się nie wywalać przez eventy)
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
            catch { }

            try
            {
                if (_hookedVm != null)
                {
                    _hookedVm.PropertyChanged -= Vm_PropertyChanged;
                    _hookedVm = null;
                }
            }
            catch { }

            try
            {
                if (_refreshDebounceTimer != null)
                {
                    _refreshDebounceTimer.Stop();
                    _refreshDebounceTimer.Tick -= RefreshDebounceTimer_Tick;
                    _refreshDebounceTimer = null;
                }
            }
            catch { }

            try
            {
                _vmForRefresh = null;
            }
            catch { }
        }

        private void ReportsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (!_eventsHooked) return;

                HookVmPropertyChanged(GetVm());

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RequestRefresh(GetVm());
                }), DispatcherPriority.Background);
            }
            catch { }
        }

        // =========================
        // VM hooks
        // =========================

        private void HookVmPropertyChanged(ReportsViewModel? vm)
        {
            if (_hookedVm == vm) return;

            if (_hookedVm != null)
                _hookedVm.PropertyChanged -= Vm_PropertyChanged;

            _hookedVm = vm;

            if (_hookedVm != null)
                _hookedVm.PropertyChanged += Vm_PropertyChanged;
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Reagujemy na zmiany dat (From/To) – to jest „źródło prawdy”
            if (e.PropertyName == nameof(ReportsViewModel.FromDate) ||
                e.PropertyName == nameof(ReportsViewModel.ToDate))
            {
                RequestRefresh(_hookedVm);
            }
        }

        // =========================
        // PeriodBar events
        // =========================

        private void PeriodBar_SearchClicked(object? sender, EventArgs e)
        {
            try { RequestRefresh(GetVm()); } catch { }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            try { RequestRefresh(GetVm()); } catch { }
        }

        private void PeriodBar_ClearClicked(object? sender, EventArgs e)
        {
            try { RequestRefresh(GetVm()); } catch { }
        }

        // =========================
        // Global PDF export
        // =========================

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVm();
            if (vm == null) return;

            try
            {
                var path = PdfExportService.ExportReportsPdf(vm, null);
                ToastService.Success($"Raport PDF zapisano na pulpicie: {path}");
            }
            catch (Exception ex)
            {
                try { ToastService.Error($"Błąd eksportu PDF: {ex.Message}"); } catch { }
            }
        }

        // =========================
        // Refresh debounce
        // =========================

        private ReportsViewModel? GetVm() => DataContext as ReportsViewModel;

        private void EnsureDebounceTimer()
        {
            if (_refreshDebounceTimer != null) return;

            _refreshDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };

            _refreshDebounceTimer.Tick += RefreshDebounceTimer_Tick;
        }

        private async void RefreshDebounceTimer_Tick(object? sender, EventArgs e)
        {
            try { _refreshDebounceTimer?.Stop(); } catch { }

            ReportsViewModel? vm;

            try
            {
                vm = _vmForRefresh;
                _vmForRefresh = null;
            }
            catch
            {
                vm = null;
            }

            if (vm == null) return;

            try
            {
                await vm.RefreshAsync();
            }
            catch (Exception ex)
            {
                try { ToastService.Error($"Błąd odświeżania raportów: {ex.Message}"); } catch { }
            }
        }

        private void RequestRefresh(ReportsViewModel? vm)
        {
            if (vm == null) return;

            try
            {
                EnsureDebounceTimer();
                _vmForRefresh = vm;

                _refreshDebounceTimer?.Stop();
                _refreshDebounceTimer?.Start();
            }
            catch
            {
                // cisza
            }
        }
    }
}
