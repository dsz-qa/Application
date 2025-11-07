using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

using Finly.Pages;
using Finly.Services;
using Finly.Views;

namespace Finly.Views
{
    public partial class ShellWindow : Window
    {
        // aktywny zestaw rozmiarów (breakpointy)
        private ResourceDictionary? _activeSizesDict;

        public ShellWindow()
        {
            InitializeComponent();
            NavigateTo("dashboard");
        }

        // ====== Hook WinAPI: gwarantuje, że WindowState=Maximized = „obszar roboczy” (taskbar widoczny) ======
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = (HwndSource)PresentationSource.FromVisual(this);
            src.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref mi))
                {
                    RECT wa = mi.rcWork;    // „work area” = bez paska zadań
                    RECT ma = mi.rcMonitor;

                    mmi.ptMaxPosition.x = Math.Abs(wa.left - ma.left);
                    mmi.ptMaxPosition.y = Math.Abs(wa.top - ma.top);
                    mmi.ptMaxSize.x = Math.Abs(wa.right - wa.left);
                    mmi.ptMaxSize.y = Math.Abs(wa.bottom - wa.top);
                }
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        // WinAPI
        private const int MONITOR_DEFAULTTONEAREST = 2;
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        // ====== Zdarzenia okna ======
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyBreakpoint(ActualWidth, ActualHeight);
            FitSidebar();

            // start jak normalna aplikacja – maksymalizacja, ale z widocznym paskiem zadań
            WindowState = WindowState.Maximized;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyBreakpoint(e.NewSize.Width, e.NewSize.Height);
            FitSidebar();
        }

        private void SidebarHost_SizeChanged(object sender, SizeChangedEventArgs e) => FitSidebar();

        // ====== Pasek tytułu / kontrolki okna ======
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { MaxRestore_Click(sender, e); return; }
            try { DragMove(); } catch { }
        }

        private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxRestore_Click(object s, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object s, RoutedEventArgs e) => Close();

        // ====== Breakpointy ======
        private void ApplyBreakpoint(double width, double height)
        {
            string key =
                (width >= 1600 && height >= 900) ? "Sizes.Large" :
                (width >= 1280 && height >= 800) ? "Sizes.Medium" :
                "Sizes.Compact";

            var dict = (ResourceDictionary)FindResource(key);
            if (!ReferenceEquals(_activeSizesDict, dict))
            {
                if (_activeSizesDict is not null)
                    Resources.MergedDictionaries.Remove(_activeSizesDict);

                Resources.MergedDictionaries.Insert(0, dict);
                _activeSizesDict = dict;

                // szerokość kolumny sidebara z klucza
                if (TryFindResource("SidebarCol.Width") is string s &&
                    double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                {
                    SidebarCol.Width = new GridLength(w);
                }
            }
        }

        /// <summary>Skaluje zawartość sidebara tak, by całość mieściła się bez scrolla.</summary>
        private void FitSidebar()
        {
            if (SidebarRoot == null || SidebarHost == null || SidebarScale == null) return;

            // pomiar naturalnej wysokości
            SidebarRoot.LayoutTransform = null;
            SidebarRoot.Measure(new Size(SidebarHost.ActualWidth, double.PositiveInfinity));
            double needed = SidebarRoot.DesiredSize.Height;
            double available = SidebarHost.ActualHeight;

            double scale = 1.0;
            if (available > 0 && needed > available) scale = available / needed;

            // bez przegięć
            if (scale < 0.7) scale = 0.7;
            if (scale > 1.0) scale = 1.0;

            SidebarScale.ScaleX = SidebarScale.ScaleY = scale;
            SidebarRoot.LayoutTransform = SidebarScale;
        }

        // ====== Nawigacja główna ======
        public void NavigateTo(string route)
        {
            var uid = UserService.CurrentUserId;
            if (uid <= 0)
            {
                var auth = new AuthWindow();
                Application.Current.MainWindow = auth;
                auth.Show();
                Close();
                return;
            }

            UserControl view = (route ?? string.Empty).ToLowerInvariant() switch
            {
                "dashboard" => new DashboardPage(uid),
                "addexpense" => new AddExpensePage(uid),
                "transactions" => new TransactionsPage(),
                "budget" or "budgets" => new BudgetsPage(),
                "categories" => new CategoriesPage(),
                "subscriptions" => new SubscriptionsPage(),
                "goals" => new GoalsPage(),
                "charts" => new ChartsPage(uid),
                "import" => new ImportPage(),
                "reports" => new ReportsPage(),
                "settings" => new SettingsPage(),
                "banks" => new BanksPage(),          // <—— DODANE
                _ => new DashboardPage(uid),
            };

            RightHost.Content = view;
        }

        // ====== Logo => dashboard ======
        private void Logo_Click(object s, RoutedEventArgs e) => NavigateToDashboard();

        private void NavigateToDashboard()
        {
            var uid = UserService.CurrentUserId;
            RightHost.Content = new DashboardPage(uid);
            SetActiveNav(null);
            SetActiveFooter(null);
        }

        // ====== Kliknięcia nawigacji ======
        private void Nav_Add_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new AddExpensePage(UserService.CurrentUserId); SetActiveNav(NavAdd); SetActiveFooter(null); }

        private void Nav_Transactions_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new TransactionsPage(); SetActiveNav(NavTransactions); SetActiveFooter(null); }

        private void Nav_Charts_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new ChartsPage(UserService.CurrentUserId); SetActiveNav(NavCharts); SetActiveFooter(null); }

        private void Nav_Budgets_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new BudgetsPage(); SetActiveNav(NavBudgets); SetActiveFooter(null); }

        private void Nav_Subscriptions_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new SubscriptionsPage(); SetActiveNav(NavSubscriptions); SetActiveFooter(null); }

        private void Nav_Goals_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new GoalsPage(); SetActiveNav(NavGoals); SetActiveFooter(null); }

        private void Nav_Categories_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new CategoriesPage(); SetActiveNav(NavCategories); SetActiveFooter(null); }

        private void Nav_Reports_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new ReportsPage(); SetActiveNav(NavReports); SetActiveFooter(null); }

        private void Nav_Import_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new ImportPage(); SetActiveNav(NavImport); SetActiveFooter(null); }

        // ====== Stopka ======
        private void OpenProfile_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new AccountPage(UserService.CurrentUserId);
            SetActiveNav(null); SetActiveFooter(FooterAccount);
        }

        private void OpenSettings_Click(object s, RoutedEventArgs e)
        { NavigateTo("settings"); SetActiveNav(null); SetActiveFooter(FooterSettings); }

        public void Nav_Logout_Click(object s, RoutedEventArgs e)
        {
            var auth = new AuthWindow();
            Application.Current.MainWindow = auth;
            auth.Show();
            Close();
        }

        // ====== Podświetlenia ======
        private void SetActiveNav(ToggleButton? active)
        {
            foreach (var child in NavContainer.Children)
                if (child is ToggleButton tb) tb.IsChecked = tb == active;
        }

        private void SetActiveFooter(ToggleButton? active)
        {
            if (FooterAccount != null) FooterAccount.IsChecked = active == FooterAccount;
            if (FooterSettings != null) FooterSettings.IsChecked = active == FooterSettings;
            if (FooterLogout != null) FooterLogout.IsChecked = false;
        }

    }
}
