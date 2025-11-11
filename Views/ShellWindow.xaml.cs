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

namespace Finly.Views
{
    public partial class ShellWindow : Window
    {
        // aktywny zestaw rozmiarów (breakpointy)
        private ResourceDictionary? _activeSizesDict;

        public ShellWindow()
        {
            InitializeComponent();
            // start: dashboard albo auth (patrz NavigateTo)
            NavigateTo("dashboard");
        }

        // ====== WinAPI: pełny ekran z poszanowaniem paska zadań ======
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
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
                MONITORINFO mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (GetMonitorInfo(monitor, ref mi))
                {
                    RECT wa = mi.rcWork;
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

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
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
            WindowState = WindowState.Maximized;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyBreakpoint(e.NewSize.Width, e.NewSize.Height);
            FitSidebar();
        }

        private void SidebarHost_SizeChanged(object? sender, SizeChangedEventArgs e) => FitSidebar();

        // ====== Pasek tytułu ======
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { MaxRestore_Click(sender, e); return; }
            try { DragMove(); } catch { /* ignoruj */ }
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

            // próbuj znaleźć słownik, ale nie zakładaj, że istnieje
            var dict = TryFindResource(key) as ResourceDictionary;

            if (dict != null && !ReferenceEquals(_activeSizesDict, dict))
            {
                if (_activeSizesDict is not null)
                    Resources.MergedDictionaries.Remove(_activeSizesDict);

                Resources.MergedDictionaries.Insert(0, dict);
                _activeSizesDict = dict;
            }

            // SidebarCol.Width – jeżeli jest w słowniku, użyj; jeśli nie, ustaw domyślnie
            double fallback = (key == "Sizes.Large") ? 380 :
                              (key == "Sizes.Medium") ? 340 : 300;

            if (TryFindResource("SidebarCol.Width") is string s &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            {
                SidebarCol.Width = new GridLength(w);
            }
            else
            {
                SidebarCol.Width = new GridLength(fallback);
            }
        }


        /// <summary>Skaluje zawartość sidebara tak, by całość mieściła się bez scrolla.</summary>
        private void FitSidebar()
        {
            if (SidebarRoot == null || SidebarHost == null || SidebarScale == null) return;

            SidebarRoot.LayoutTransform = null;
            SidebarRoot.Measure(new Size(SidebarHost.ActualWidth, double.PositiveInfinity));
            double needed = SidebarRoot.DesiredSize.Height;
            double available = SidebarHost.ActualHeight;

            double scale = 1.0;
            if (available > 0 && needed > available)
                scale = available / needed;

            if (scale < 0.7) scale = 0.7;
            if (scale > 1.0) scale = 1.0;

            SidebarScale.ScaleX = SidebarScale.ScaleY = scale;
            SidebarRoot.LayoutTransform = SidebarScale;
        }

        // ====== Nawigacja ======
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
                "subscriptions" => new InvestmentsPage(),
                "goals" => new GoalsPage(),
                "charts" => new ChartsPage(uid),
                "import" => new ImportPage(),
                "reports" => new ReportsPage(),
                "settings" => new SettingsPage(),
                "banks" => new BanksPage(),
                "envelopes" => new EnvelopesPage(uid),
                _ => new DashboardPage(uid),
            };

            RightHost.Content = view;
        }

        private void Logo_Click(object s, RoutedEventArgs e) => NavigateToDashboard();

        private void NavigateToDashboard()
        {
            var uid = UserService.CurrentUserId;
            RightHost.Content = new DashboardPage(uid);
            SetActiveNav(null);
            SetActiveFooter(null);
        }

        // Kliknięcia NAV
        private void Nav_Home_Click(object sender, RoutedEventArgs e)
        {
            // Tu ładujesz stronę główną (DashboardPage). Jeśli masz już metodę SelectNav,
            // użyj jej – ważne, by odznaczyć inne ToggleButtony i wstawić zawartość.
            UncheckAllNav();
            NavHome.IsChecked = true;
            RightHost.Content = new Finly.Pages.DashboardPage(UserService.GetCurrentUserId());
        }

        private void Nav_Add_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new AddExpensePage(UserService.CurrentUserId); SetActiveNav(NavAdd); SetActiveFooter(null); }

        private void Nav_Transactions_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new TransactionsPage(); SetActiveNav(NavTransactions); SetActiveFooter(null); }

        private void Nav_Charts_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new ChartsPage(UserService.CurrentUserId); SetActiveNav(NavCharts); SetActiveFooter(null); }

        private void Nav_Budgets_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new BudgetsPage(); SetActiveNav(NavBudgets); SetActiveFooter(null); }

        private void Nav_Goals_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new GoalsPage(); SetActiveNav(NavGoals); SetActiveFooter(null); }

        private void Nav_Categories_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new CategoriesPage(); SetActiveNav(NavCategories); SetActiveFooter(null); }

        private void Nav_Reports_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new ReportsPage(); SetActiveNav(NavReports); SetActiveFooter(null); }

        private void Nav_Import_Click(object s, RoutedEventArgs e)
        { RightHost.Content = new ImportPage(); SetActiveNav(NavImport); SetActiveFooter(null); }

        private void Nav_Banks_Click(object s, RoutedEventArgs e)
        { NavigateTo("banks"); SetActiveNav(NavBanks); SetActiveFooter(null); }

        private void Nav_Envelopes_Click(object s, RoutedEventArgs e)
        { NavigateTo("envelopes"); SetActiveNav(NavEnvelopes); SetActiveFooter(null); }

private void Nav_Loans_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // odznacz inne przyciski nawigacji jeżeli masz taką logikę
        RightHost.Content = new LoansPage();
    }

    private void Nav_Investments_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        RightHost.Content = new InvestmentsPage();
    }


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

        // ====== Podświetlenia (bezpieczne) ======
        private void SetActiveNav(ToggleButton? active)
        {
            if (NavContainer == null) return;
            foreach (var child in NavContainer.Children)
                if (child is ToggleButton tb)
                    tb.IsChecked = (tb == active);
        }

        private void SetActiveFooter(ToggleButton? active)
        {
            if (FooterAccount != null) FooterAccount.IsChecked = active == FooterAccount;
            if (FooterSettings != null) FooterSettings.IsChecked = active == FooterSettings;
            if (FooterLogout != null) FooterLogout.IsChecked = false;
        }
    }
}

