using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Finly.Pages;
using Finly.Services.Features;
using Finly.Services.SpecificPages;

namespace Finly.Views
{
    public partial class ShellWindow : Window
    {
        private ResourceDictionary? _activeSizesDict;

        public ShellWindow()
        {
            InitializeComponent();
            GoHome(); // domyślnie: Panel główny
        }

        // ===== WinAPI – pełny ekran z poszanowaniem paska zadań =====
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

        // ===== Zdarzenia okna / responsywność =====
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

        // ===== Pasek tytułu =====
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Nie przeciągaj, jeśli kliknięto kontrolkę
            if (e.OriginalSource is DependencyObject d &&
                (FindParent<Button>(d) != null || FindParent<TextBlock>(d) != null || FindParent<Image>(d) != null))
                return;

            if (e.ClickCount == 2) { MaxRestore_Click(sender, e); return; }
            try { DragMove(); } catch { /* ignoruj */ }
        }

        private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxRestore_Click(object s, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object s, RoutedEventArgs e) => Close();

        // ===== Breakpointy =====
        private void ApplyBreakpoint(double width, double height)
        {
            string key =
                (width >= 1600 && height >= 900) ? "Sizes.Large" :
                (width >= 1280 && height >= 800) ? "Sizes.Medium" :
                "Sizes.Compact";

            var dict = TryFindResource(key) as ResourceDictionary;
            if (dict != null && !ReferenceEquals(_activeSizesDict, dict))
            {
                if (_activeSizesDict is not null)
                    Resources.MergedDictionaries.Remove(_activeSizesDict);

                Resources.MergedDictionaries.Insert(0, dict);
                _activeSizesDict = dict;
            }

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

        // ===== Nawigacja wysokiego poziomu =====
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
                "dashboard" or "home" => new DashboardPage(uid),
                "addexpense" => new AddExpensePage(uid),
                "transactions" => new TransactionsPage(),
                "budget" or "budgets" => new BudgetsPage(),
                "categories" => new CategoriesPage(),
                "goals" => new GoalsPage(),
                "charts" => new ChartsPage(),
                "reports" => new ReportsPage(),
                "settings" => new SettingsPage(),
                "banks" => new BanksPage(),
                "envelopes" => new EnvelopesPage(uid),
                "loans" => new LoansPage(),
                "investments" => new InvestmentsPage(),
                _ => new DashboardPage(uid),
            };

            RightHost.Content = view;
        }

        private void GoHome()
        {
            RightHost.Content = new DashboardPage(UserService.GetCurrentUserId());
            SetActiveNav(NavHome);
            SetActiveFooter(null);
        }

        // ===== Kliknięcia NAV (lewy sidebar) =====
        private void Nav_Home_Click(object sender, RoutedEventArgs e)
        {
            GoHome();
        }

        private void Nav_Add_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new AddExpensePage(UserService.CurrentUserId);
            SetActiveNav(NavAdd);
            SetActiveFooter(null);
        }

        private void Nav_Transactions_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new TransactionsPage();
            SetActiveNav(NavTransactions);
            SetActiveFooter(null);
        }

        private void Nav_Charts_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new ChartsPage();
            SetActiveNav(NavCharts);
            SetActiveFooter(null);
        }

        private void Nav_Budgets_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new BudgetsPage();
            SetActiveNav(NavBudgets);
            SetActiveFooter(null);
        }

        private void Nav_Goals_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new GoalsPage();
            SetActiveNav(NavGoals);
            SetActiveFooter(null);
        }

        private void Nav_Categories_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new CategoriesPage();
            SetActiveNav(NavCategories);
            SetActiveFooter(null);
        }

        private void Nav_Reports_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new ReportsPage();
            SetActiveNav(NavReports);
            SetActiveFooter(null);
        }


        private void Nav_Banks_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("banks");
            SetActiveNav(NavBanks);
            SetActiveFooter(null);
        }

        private void Nav_Envelopes_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("envelopes");
            SetActiveNav(NavEnvelopes);
            SetActiveFooter(null);
        }

        private void Nav_Loans_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new LoansPage();
            SetActiveNav(NavLoans);
            SetActiveFooter(null);
        }

        private void Nav_Investments_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new InvestmentsPage();
            SetActiveNav(NavInvestments);
            SetActiveFooter(null);
        }

        // ===== Stopka =====
        private void OpenProfile_Click(object s, RoutedEventArgs e)
        {
            RightHost.Content = new AccountPage(UserService.CurrentUserId);
            SetActiveNav(null);
            SetActiveFooter(FooterAccount);
        }

        private void OpenSettings_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("settings");
            SetActiveNav(null);
            SetActiveFooter(FooterSettings);
        }

        private void Nav_Logout_Click(object sender, RoutedEventArgs e)
        {
            // Po wylogowaniu nie auto-logujemy już tego użytkownika
            SettingsService.LastUserId = null;

            UserService.ClearCurrentUser();

            var auth = new AuthWindow();
            Application.Current.MainWindow = auth;
            auth.Show();

            Close();
        }

        // ===== Podświetlenia / pomocnicze =====
        private void SetActiveNav(ToggleButton? active)
        {
            if (NavContainer == null) return;

            foreach (var tb in FindVisualChildren<ToggleButton>(NavContainer))
                tb.IsChecked = false;

            if (active != null)
            {
                active.IsChecked = true;
                return;
            }

            if (RightHost.Content is DashboardPage && NavHome != null)
                NavHome.IsChecked = true;
        }

        private void SetActiveFooter(ToggleButton? active)
        {
            if (FooterAccount != null) FooterAccount.IsChecked = active == FooterAccount;
            if (FooterSettings != null) FooterSettings.IsChecked = active == FooterSettings;
            if (FooterLogout != null) FooterLogout.IsChecked = false;
        }

        private void UncheckAllNav()
        {
            SetActiveNav(null);
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T match) yield return match;
                foreach (var sub in FindVisualChildren<T>(child))
                    yield return sub;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                var parent = VisualTreeHelper.GetParent(child);
                if (parent is T p) return p;
                child = parent;
            }
            return null;
        }
    }
}
