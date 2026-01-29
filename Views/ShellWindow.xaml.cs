using Finly.Pages;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
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

            if (PresentationSource.FromVisual(this) is HwndSource src)
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
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
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
            // ✅ 1) Globalna realizacja rat i innych zaplanowanych płatności
            try
            {
                var uid = UserService.CurrentUserId; // tu już masz to w appce używane
                if (uid > 0)
                    DatabaseService.ProcessDuePlannedTransactions(uid, DateTime.Today);
            }
            catch
            {
                // celowo: nie wywracamy okna
            }

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
            if (e.OriginalSource is DependencyObject d &&
                (FindParent<Button>(d) != null || FindParent<TextBlock>(d) != null || FindParent<Image>(d) != null))
                return;

            if (e.ClickCount == 2) { MaxRestore_Click(sender, e); return; }
            try { DragMove(); } catch { }
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

        // =====================================================================
        // NAWIGACJA – JEDNO ŹRÓDŁO PRAWDY (MainFrame)
        // =====================================================================

        /// <summary>Nawigacja do konkretnej strony (UserControl) w MainFrame.</summary>
        public void NavigateTo(UserControl page)
        {
            if (page == null) return;
            if (MainFrame == null) return;

            MainFrame.Navigate(page);
        }

        /// <summary>Nawigacja po "route" (string). Zwraca stronę i podpina do MainFrame.</summary>
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

            string r = (route ?? string.Empty).Trim().ToLowerInvariant();

            UserControl view = r switch
            {
                "dashboard" or "home" => new DashboardPage(uid),

                // AddExpensePage wymaga uid
                "addexpense" or "add" => new AddExpensePage(uid),

                // Transakcje bez uid
                "transactions" or "transakcje" => new TransactionsPage(),

                // ✅ KLUCZOWA POPRAWKA: BudgetsPage MUSI dostać uid
                "budget" or "budgets" or "budzety" => new BudgetsPage(uid),

                "categories" or "kategorie" => new CategoriesPage(),
                "goals" or "cele" => new GoalsPage(),
                "charts" or "statystyki" => new ChartsPage(),
                "reports" or "raporty" => new ReportsPage(),
                "settings" or "ustawienia" => new SettingsPage(),

                "banks" or "kontabankowe" => new BanksPage(),

                // EnvelopesPage wymaga uid
                "envelopes" or "koperty" => new EnvelopesPage(uid),

                "loans" or "kredyty" => new LoansPage(),
                "investments" or "inwestycje" => new InvestmentsPage(),

                _ => new DashboardPage(uid),
            };

            NavigateTo(view);
            ApplyActiveHighlightForRoute(r);
        }

        private void GoHome()
        {
            NavigateTo("home");
            SetActiveFooter(null);
        }

        // ===== Kliknięcia NAV (lewy sidebar) =====
        private void Nav_Home_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo("home");
            SetActiveFooter(null);
        }

        private void Nav_Add_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("addexpense");
            SetActiveFooter(null);
        }

        private void Nav_Transactions_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("transactions");
            SetActiveFooter(null);
        }

        private void Nav_Charts_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("charts");
            SetActiveFooter(null);
        }

        private void Nav_Budgets_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("budgets");
            SetActiveFooter(null);
        }

        private void Nav_Goals_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("goals");
            SetActiveFooter(null);
        }

        private void Nav_Categories_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("categories");
            SetActiveFooter(null);
        }

        private void Nav_Reports_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("reports");
            SetActiveFooter(null);
        }

        private void Nav_Banks_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("banks");
            SetActiveFooter(null);
        }

        private void Nav_Envelopes_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("envelopes");
            SetActiveFooter(null);
        }

        private void Nav_Loans_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("loans");
            SetActiveFooter(null);
        }

        private void Nav_Investments_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("investments");
            SetActiveFooter(null);
        }

        // ===== Stopka =====
        private void OpenProfile_Click(object s, RoutedEventArgs e)
        {
            NavigateTo(new AccountPage(UserService.CurrentUserId));
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
            SettingsService.LastUserId = null;
            UserService.ClearCurrentUser();

            var auth = new AuthWindow();
            Application.Current.MainWindow = auth;
            auth.Show();

            Close();
        }

        // =====================================================================
        // PODŚWIETLANIA
        // =====================================================================

        private void ApplyActiveHighlightForRoute(string r)
        {
            SetActiveFooter(null);

            switch (r)
            {
                case "dashboard":
                case "home":
                    SetActiveNav(NavHome);
                    break;

                case "addexpense":
                case "add":
                    SetActiveNav(NavAdd);
                    break;

                case "transactions":
                case "transakcje":
                    SetActiveNav(NavTransactions);
                    break;

                case "budgets":
                case "budget":
                case "budzety":
                    SetActiveNav(NavBudgets);
                    break;

                case "categories":
                case "kategorie":
                    SetActiveNav(NavCategories);
                    break;

                case "charts":
                case "statystyki":
                    SetActiveNav(NavCharts);
                    break;

                case "reports":
                case "raporty":
                    SetActiveNav(NavReports);
                    break;

                case "loans":
                case "kredyty":
                    SetActiveNav(NavLoans);
                    break;

                case "goals":
                case "cele":
                    SetActiveNav(NavGoals);
                    break;

                case "investments":
                case "inwestycje":
                    SetActiveNav(NavInvestments);
                    break;

                case "banks":
                case "kontabankowe":
                    SetActiveNav(NavBanks);
                    break;

                case "envelopes":
                case "koperty":
                    SetActiveNav(NavEnvelopes);
                    break;

                default:
                    SetActiveNav(NavHome);
                    break;
            }
        }

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

            if (MainFrame?.Content is DashboardPage && NavHome != null)
                NavHome.IsChecked = true;
        }

        private void SetActiveFooter(ToggleButton? active)
        {
            if (FooterAccount != null) FooterAccount.IsChecked = active == FooterAccount;
            if (FooterSettings != null) FooterSettings.IsChecked = active == FooterSettings;
            if (FooterLogout != null) FooterLogout.IsChecked = false;
        }

        // ===== Helpery visual tree =====
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
