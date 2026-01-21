using Finly.Services;
using Finly.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

// kontrolki zakładek
using Finly.Views.Controls.ReportsPageControls;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        private bool _eventsHooked;

        private DispatcherTimer? _refreshDebounceTimer;
        private ReportsViewModel? _vmForRefresh;
        private ReportsViewModel? _hookedVm;

        private bool _tabsInitialized;
        private TabControl? _tabsHost;

        public ReportsPage()
        {
            InitializeComponent();

            Loaded += ReportsPage_Loaded;
            Unloaded += ReportsPage_Unloaded;
            DataContextChanged += ReportsPage_DataContextChanged;
        }

        // kompatybilność, jeśli gdzieś istnieje taki konstruktor
        public ReportsPage(int userId) : this() { }

        private void ReportsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_eventsHooked) return;
            _eventsHooked = true;

            // Nie nadpisuj DataContext, jeśli ktoś już go podał (Shell/Nawigacja)
            if (DataContext is not ReportsViewModel)
                DataContext = new ReportsViewModel();

            EnsureDebounceTimer();

            // PeriodBar events (jeśli istnieje w XAML)
            if (PeriodBar != null)
            {
                PeriodBar.SearchClicked += PeriodBar_SearchClicked;
                PeriodBar.RangeChanged += PeriodBar_RangeChanged;
                PeriodBar.ClearClicked += PeriodBar_ClearClicked;
            }

            HookVmPropertyChanged(GetVm());

            // Tab host + lazy init
            _tabsHost = FindTabsHost();
            if (_tabsHost != null)
                _tabsHost.SelectionChanged += TabsHost_SelectionChanged;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureTabsCreated();
                RequestRefresh(GetVm());
            }), DispatcherPriority.Background);
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
            catch { /* celowo cisza */ }

            try
            {
                if (_tabsHost != null)
                    _tabsHost.SelectionChanged -= TabsHost_SelectionChanged;
            }
            catch { /* celowo cisza */ }

            try
            {
                if (_hookedVm != null)
                {
                    _hookedVm.PropertyChanged -= Vm_PropertyChanged;
                    _hookedVm = null;
                }
            }
            catch { /* celowo cisza */ }

            try
            {
                if (_refreshDebounceTimer != null)
                {
                    _refreshDebounceTimer.Stop();
                    _refreshDebounceTimer.Tick -= RefreshDebounceTimer_Tick;
                    _refreshDebounceTimer = null;
                }
            }
            catch { /* celowo cisza */ }

            _tabsHost = null;
            _vmForRefresh = null;
            _tabsInitialized = false;
        }

        private void ReportsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!_eventsHooked) return;

            HookVmPropertyChanged(GetVm());

            Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureTabsCreated();
                RequestRefresh(GetVm());
            }), DispatcherPriority.Background);
        }

        // =========================
        // Tabs: lazy init
        // =========================

        private void EnsureTabsCreated()
        {
            if (_tabsInitialized) return;

            var tabs = _tabsHost ?? FindTabsHost();
            if (tabs == null) return;

            try
            {
                foreach (var obj in tabs.Items)
                {
                    if (obj is not TabItem ti) continue;
                    if (ti.Content != null) continue; // już wstawione

                    var header = (ti.Header?.ToString() ?? "").Trim();

                    UserControl? content = header switch
                    {
                        "Przegląd ogólny" => new OverviewTab(),
                        "Budżety" => new BudgetsTab(),
                        "Kredyty" => new LoansTab(),
                        "Cele" => new GoalsTab(),
                        "Inwestycje" => new InvestmentsTab(),
                        "Symulacja" => new SimulationTab(),
                        _ => null
                    };

                    if (content == null) continue;

                    // NIE wstrzykujemy Resources (to powoduje wyjątki rodzica ResourceDictionary)
                    // Warunek: style typu Card/AccentCard/MiniTable* muszą być globalnie w App.xaml (MergedDictionaries).

                    ti.Content = content;
                }

                _tabsInitialized = true;
            }
            catch
            {
                // Jeśli coś poleci, to i tak lepiej, żeby strona nie wysadzała całej aplikacji.
                // Ale przy poprawnych globalnych zasobach, tu nie powinno być wyjątków.
            }
        }

        private void TabsHost_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Hook na przyszłość (np. odświeżaj tylko aktywną zakładkę).
            // Teraz: nic nie robimy, bo VM i tak odświeża komplet danych.
        }

        private TabControl? FindTabsHost()
        {
            // Preferowany wariant: nazwij TabControl w XAML jako x:Name="ReportsTabs"
            if (FindName("ReportsTabs") is TabControl named)
                return named;

            return FindFirstChildTabControl(this);
        }

        private static TabControl? FindFirstChildTabControl(DependencyObject root)
        {
            try
            {
                var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    if (child is TabControl tc) return tc;

                    var nested = FindFirstChildTabControl(child);
                    if (nested != null) return nested;
                }
            }
            catch { /* celowo cisza */ }

            return null;
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

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
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
                var path = PdfExportService.ExportReportsPdf(vm);
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
                // celowo cisza
            }
        }
    }
}
