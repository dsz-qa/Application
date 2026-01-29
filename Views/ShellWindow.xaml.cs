using Finly.Pages;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Finly.Views
{
    public partial class ShellWindow : Window
    {
        private ResourceDictionary? _activeSizesDict;

        // =========================
        // PAGE CACHE (per user)
        // =========================
        private readonly Dictionary<string, UserControl> _pageCache = new();
        private int _cachedUid = -1;

        // =========================
        // NAVIGATION: cancel + gate + "last wins"
        // =========================
        private readonly SemaphoreSlim _navGate = new(1, 1);
        private CancellationTokenSource? _navCts;
        private string _currentRouteNormalized = string.Empty;

        // "ostatni klik wygrywa" nawet jeśli taski się skolejkowały
        private int _navSeq = 0;

        public ShellWindow()
        {
            InitializeComponent();

            if (MainFrame != null)
            {
                MainFrame.Navigated += MainFrame_Navigated;
                MainFrame.NavigationFailed += MainFrame_NavigationFailed;
            }

            GoHome();
        }

        // =========================
        // Frame handlers
        // =========================
        private void MainFrame_Navigated(object? sender, NavigationEventArgs e)
        {
            try
            {
                MainFrame?.NavigationService?.RemoveBackEntry();
            }
            catch { }
        }

        private void MainFrame_NavigationFailed(object? sender, NavigationFailedEventArgs e)
        {
            e.Handled = true;
        }

        // =========================
        // WinAPI – pełny ekran z poszanowaniem paska zadań
        // =========================
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

        // =========================
        // Zdarzenia okna / responsywność
        // =========================
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Ciężkie rzeczy poza UI
            try
            {
                var uid = UserService.CurrentUserId;
                if (uid > 0)
                {
                    _ = Task.Run(() =>
                    {
                        try { DatabaseService.ProcessDuePlannedTransactions(uid, DateTime.Today); }
                        catch { }
                    });
                }
            }
            catch { }

            ApplyBreakpoint(ActualWidth, ActualHeight);

            await Dispatcher.InvokeAsync(() => FitSidebar(), System.Windows.Threading.DispatcherPriority.Background);

            // ❌ NIE ustawiamy tu WindowState = Maximized (to robi mrugnięcie).
            // Zrób to w XAML: WindowState="Maximized"
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyBreakpoint(e.NewSize.Width, e.NewSize.Height);
            Dispatcher.InvokeAsync(() => FitSidebar(), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SidebarHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() => FitSidebar(), System.Windows.Threading.DispatcherPriority.Background);
        }

        // =========================
        // Pasek tytułu
        // =========================
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

        // =========================
        // Breakpointy
        // =========================
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

        private double _lastSidebarScale = 1.0;
        private DateTime _lastSidebarFitAt = DateTime.MinValue;

        private void FitSidebar()
        {
            if (SidebarRoot == null || SidebarHost == null || SidebarScale == null) return;

            var now = DateTime.UtcNow;
            if ((now - _lastSidebarFitAt).TotalMilliseconds < 50)
                return;
            _lastSidebarFitAt = now;

            SidebarRoot.Measure(new Size(SidebarHost.ActualWidth, double.PositiveInfinity));

            double needed = SidebarRoot.DesiredSize.Height;
            double available = SidebarHost.ActualHeight;

            double scale = 1.0;
            if (available > 0 && needed > available)
                scale = available / needed;

            if (scale < 0.7) scale = 0.7;
            if (scale > 1.0) scale = 1.0;

            if (Math.Abs(scale - _lastSidebarScale) < 0.02)
                return;

            _lastSidebarScale = scale;
            SidebarScale.ScaleX = SidebarScale.ScaleY = scale;
        }

        // =====================================================================
        // NAWIGACJA
        // =====================================================================

        private static string NormalizeRoute(string? route)
            => (route ?? string.Empty).Trim().ToLowerInvariant();

        private static string MakeKey(int uid, string routeNormalized)
            => $"{uid}:{routeNormalized}";

        private void ResetCacheIfUserChanged(int uid)
        {
            if (_cachedUid == uid) return;
            _pageCache.Clear();
            _cachedUid = uid;
        }

        private UserControl GetOrCreatePage(string routeNormalized, int uid)
        {
            ResetCacheIfUserChanged(uid);

            var key = MakeKey(uid, routeNormalized);
            if (_pageCache.TryGetValue(key, out var cached))
                return cached;

            UserControl created = routeNormalized switch
            {
                "dashboard" or "home" => new DashboardPage(uid),
                "addexpense" or "add" => new AddExpensePage(uid),

                "transactions" or "transakcje" => new TransactionsPage(),

                "budget" or "budgets" or "budzety" => new BudgetsPage(uid),

                "categories" or "kategorie" => new CategoriesPage(),
                "goals" or "cele" => new GoalsPage(),
                "charts" or "statystyki" => new ChartsPage(),
                "reports" or "raporty" => new ReportsPage(),
                "settings" or "ustawienia" => new SettingsPage(),

                "banks" or "kontabankowe" => new BanksPage(),
                "envelopes" or "koperty" => new EnvelopesPage(uid),

                "loans" or "kredyty" => new LoansPage(),
                "investments" or "inwestycje" => new InvestmentsPage(),

                "account" => new AccountPage(uid),

                _ => new DashboardPage(uid),
            };

            _pageCache[key] = created;
            return created;
        }

        public void NavigateTo(string route) => _ = NavigateToAsync(route);

        private async Task NavigateToAsync(string route)
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

            string r = NormalizeRoute(route);
            if (r == _currentRouteNormalized) return;

            // ✅ generujemy numer żądania (ostatnie wygrywa)
            int mySeq = Interlocked.Increment(ref _navSeq);

            // ✅ anuluj poprzednie, aby nie stały w kolejce
            _navCts?.Cancel();
            _navCts?.Dispose();
            _navCts = new CancellationTokenSource();
            var ct = _navCts.Token;

            try
            {
                // ✅ KLUCZ: czekamy na gate z tokenem, więc backlog nie powstaje
                await _navGate.WaitAsync(ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                // jeśli w międzyczasie wpadło nowsze żądanie, wychodzimy natychmiast
                if (mySeq != Volatile.Read(ref _navSeq)) return;
                if (ct.IsCancellationRequested) return;

                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                if (ct.IsCancellationRequested) return;
                if (mySeq != Volatile.Read(ref _navSeq)) return;

                var view = GetOrCreatePage(r, uid);

                if (MainFrame != null && !ReferenceEquals(MainFrame.Content, view))
                    MainFrame.Navigate(view);

                _currentRouteNormalized = r;
                ApplyActiveHighlightForRoute(r);
            }
            catch
            {
                // nie wywracamy okna
            }
            finally
            {
                _navGate.Release();
            }
        }

        private void GoHome()
        {
            NavigateTo("home");
            SetActiveFooter(null);
        }

        // =========================
        // Kliknięcia NAV
        // =========================
        private void Nav_Home_Click(object sender, RoutedEventArgs e) { NavigateTo("home"); SetActiveFooter(null); }
        private void Nav_Add_Click(object s, RoutedEventArgs e) { NavigateTo("addexpense"); SetActiveFooter(null); }
        private void Nav_Transactions_Click(object s, RoutedEventArgs e) { NavigateTo("transactions"); SetActiveFooter(null); }
        private void Nav_Charts_Click(object s, RoutedEventArgs e) { NavigateTo("charts"); SetActiveFooter(null); }
        private void Nav_Budgets_Click(object s, RoutedEventArgs e) { NavigateTo("budgets"); SetActiveFooter(null); }
        private void Nav_Goals_Click(object s, RoutedEventArgs e) { NavigateTo("goals"); SetActiveFooter(null); }
        private void Nav_Categories_Click(object s, RoutedEventArgs e) { NavigateTo("categories"); SetActiveFooter(null); }
        private void Nav_Reports_Click(object s, RoutedEventArgs e) { NavigateTo("reports"); SetActiveFooter(null); }
        private void Nav_Banks_Click(object s, RoutedEventArgs e) { NavigateTo("banks"); SetActiveFooter(null); }
        private void Nav_Envelopes_Click(object s, RoutedEventArgs e) { NavigateTo("envelopes"); SetActiveFooter(null); }
        private void Nav_Loans_Click(object s, RoutedEventArgs e) { NavigateTo("loans"); SetActiveFooter(null); }
        private void Nav_Investments_Click(object s, RoutedEventArgs e) { NavigateTo("investments"); SetActiveFooter(null); }

        // =========================
        // Stopka
        // =========================
        private void OpenProfile_Click(object s, RoutedEventArgs e)
        {
            NavigateTo("account");
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
            _pageCache.Clear();
            _cachedUid = -1;

            _navCts?.Cancel();
            _navCts?.Dispose();
            _navCts = null;

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
                case "home": SetActiveNav(NavHome); break;

                case "addexpense":
                case "add": SetActiveNav(NavAdd); break;

                case "transactions":
                case "transakcje": SetActiveNav(NavTransactions); break;

                case "budgets":
                case "budget":
                case "budzety": SetActiveNav(NavBudgets); break;

                case "categories":
                case "kategorie": SetActiveNav(NavCategories); break;

                case "charts":
                case "statystyki": SetActiveNav(NavCharts); break;

                case "reports":
                case "raporty": SetActiveNav(NavReports); break;

                case "loans":
                case "kredyty": SetActiveNav(NavLoans); break;

                case "goals":
                case "cele": SetActiveNav(NavGoals); break;

                case "investments":
                case "inwestycje": SetActiveNav(NavInvestments); break;

                case "banks":
                case "kontabankowe": SetActiveNav(NavBanks); break;

                case "envelopes":
                case "koperty": SetActiveNav(NavEnvelopes); break;

                case "account":
                    SetActiveNav(null);
                    SetActiveFooter(FooterAccount);
                    break;

                case "settings":
                case "ustawienia":
                    SetActiveNav(null);
                    SetActiveFooter(FooterSettings);
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

        // =========================
        // Helpery visual tree
        // =========================
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
