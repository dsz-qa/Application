using Finly.Models;
using Finly.Services;
using Finly.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Finly.Views
{
    public partial class AuthWindow : Window
    {
        private AuthViewModel VM => (AuthViewModel)DataContext;
        private bool _forceFullscreen = false;

        public AuthWindow()
        {
            InitializeComponent();
            DataContext = new AuthViewModel();
            KeyDown += Window_KeyDown;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
            src.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (!_forceFullscreen && msg == WM_GETMINMAXINFO)
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
        [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

        // Pasek tytułu
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInside<Button>(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) { MaxRestore_Click(sender, e); return; }
            try { DragMove(); } catch { }
        }
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaxRestore_Click(object sender, RoutedEventArgs e) => WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void CloseButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (CloseGlyph != null) CloseGlyph.Foreground = (Brush)FindResource("ErrorBrush");
        }
        private void CloseButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (CloseGlyph != null) CloseGlyph.Foreground = (Brush)FindResource("App.Foreground");
        }

        private static bool IsInside<T>(DependencyObject? src) where T : DependencyObject
        {
            while (src != null)
            {
                if (src is T) return true;
                src = VisualTreeHelper.GetParent(src);
            }
            return false;
        }

        private static T? FindParent<T>(DependencyObject start) where T : DependencyObject
        {
            var p = VisualTreeHelper.GetParent(start);
            while (p != null && p is not T) p = VisualTreeHelper.GetParent(p);
            return p as T;
        }

        // Przełączanie paneli
        private void SwitchToRegister_Click(object sender, RoutedEventArgs e)
        {
            AccountTypeChooser.Visibility = Visibility.Visible;

            Dispatcher.InvokeAsync(() =>
            {
                AccountTypeChooser.UpdateLayout();
                AccountTypeChooser.BringIntoView();
                var sv = FindParent<ScrollViewer>(AccountTypeChooser);
                sv?.ScrollToVerticalOffset(sv.VerticalOffset + 20);
            });
        }

        private void ChoosePersonal_Click(object sender, RoutedEventArgs e)
        {
            VM.AccountType = AccountType.Personal;
            AccountTypeChooser.Visibility = Visibility.Collapsed;
            VM.SwitchToRegister();
        }

        private void ChooseBusiness_Click(object sender, RoutedEventArgs e)
        {
            VM.AccountType = AccountType.Business;
            AccountTypeChooser.Visibility = Visibility.Collapsed;
            VM.SwitchToRegister();
        }

        private void SwitchToLogin_Click(object sender, RoutedEventArgs e) => VM.SwitchToLogin();

        // Logowanie
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var pwd = PwdLoginText.Visibility == Visibility.Visible ? PwdLoginText.Text : PwdLogin.Password;
            if (VM.Login(pwd))
            {
                var userId = VM.LoggedInUserId > 0
                    ? VM.LoggedInUserId
                    : UserService.GetUserIdByLogin(VM.Username);

                OnLoginSuccess(userId);
            }
            else
            {
                LoginUsername.Focus();
                LoginUsername.SelectAll();
            }
        }

        private void ClearLoginError()
        {
            VM.LoginIsError = false;
            VM.LoginMessage = string.Empty;
        }
        private void LoginUsername_TextChanged(object sender, TextChangedEventArgs e) => ClearLoginError();
        private void PwdLogin_PasswordChanged(object sender, RoutedEventArgs e) => ClearLoginError();
        private void PwdLoginText_TextChanged(object sender, TextChangedEventArgs e) => ClearLoginError();

        private void OnLoginSuccess(int userId)
        {
            if (userId <= 0)
            {
                VM.LoginIsError = true;
                VM.LoginMessage = "Nie udało się odczytać identyfikatora użytkownika.";
                return;
            }

            UserService.CurrentUserId = userId;
            UserService.CurrentUserName = VM.Username;
            UserService.CurrentUserEmail = UserService.GetEmail(userId);

            bool onboarded = DatabaseService.IsUserOnboarded(userId);

            Window next = onboarded
                ? new ShellWindow()
                : new FirstRunWindow(userId);

            Application.Current.MainWindow = next;
            next.Show();
            Close();
        }

        // Rejestracja
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var pass = PwdRegText.Visibility == Visibility.Visible ? PwdRegText.Text : PwdReg.Password;
            var confirm = PwdRegConfirmText.Visibility == Visibility.Visible ? PwdRegConfirmText.Text : PwdRegConfirm.Password;

            if (VM.Register(pass, confirm))
                RegShowPassword_Unchecked(null!, null!);
        }

        // Pokaż/ukryj hasło — LOGOWANIE
        private void LoginShowPassword_Checked(object sender, RoutedEventArgs e)
        {
            PwdLoginText.Text = PwdLogin.Password;
            PwdLogin.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(PwdLogin, 0);
            PwdLoginText.Visibility = Visibility.Visible;
            Panel.SetZIndex(PwdLoginText, 1);
            PwdLoginText.Focus();
            PwdLoginText.CaretIndex = PwdLoginText.Text.Length;
        }

        private void LoginShowPassword_Unchecked(object sender, RoutedEventArgs e)
        {
            PwdLogin.Password = PwdLoginText.Text ?? string.Empty;
            PwdLoginText.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(PwdLoginText, 0);
            PwdLogin.Visibility = Visibility.Visible;
            Panel.SetZIndex(PwdLogin, 1);
            PwdLogin.Focus();
            PwdLogin.SelectAll();
        }

        // Pokaż/ukryj hasło — REJESTRACJA
        private bool _syncingRegPasswords = false;
        private static void SetTextIfDifferent(TextBox tb, string value) { if (tb.Text == value) return; tb.Text = value ?? string.Empty; tb.CaretIndex = tb.Text.Length; }
        private static void SetPasswordIfDifferent(PasswordBox pb, string value) { if (pb.Password == (value ?? string.Empty)) return; pb.Password = value ?? string.Empty; }

        private void PwdReg_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingRegPasswords) return;
            _syncingRegPasswords = true;
            var val = ((PasswordBox)sender).Password;
            if (VM.Password != val) VM.Password = val;
            if (PwdRegText.Visibility == Visibility.Visible) SetTextIfDifferent(PwdRegText, val);
            _syncingRegPasswords = false;
        }

        private void PwdRegText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingRegPasswords) return;
            _syncingRegPasswords = true;
            var val = ((TextBox)sender).Text;
            if (VM.Password != val) VM.Password = val;
            if (PwdReg.Visibility == Visibility.Visible) SetPasswordIfDifferent(PwdReg, val);
            _syncingRegPasswords = false;
        }

        private void PwdRegConfirm_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingRegPasswords) return;
            _syncingRegPasswords = true;
            var val = ((PasswordBox)sender).Password;
            if (VM.RepeatPassword != val) VM.RepeatPassword = val;
            if (PwdRegConfirmText.Visibility == Visibility.Visible) SetTextIfDifferent(PwdRegConfirmText, val);
            _syncingRegPasswords = false;
        }

        private void PwdRegConfirmText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingRegPasswords) return;
            _syncingRegPasswords = true;
            var val = ((TextBox)sender).Text;
            if (VM.RepeatPassword != val) VM.RepeatPassword = val;
            if (PwdRegConfirm.Visibility == Visibility.Visible) SetPasswordIfDifferent(PwdRegConfirm, val);
            _syncingRegPasswords = false;
        }

        private void RegShowPassword_Unchecked(object? sender, RoutedEventArgs? e)
        {
            if (PwdRegText != null && PwdReg != null) PwdReg.Password = PwdRegText.Text ?? string.Empty;
            if (PwdRegConfirmText != null && PwdRegConfirm != null) PwdRegConfirm.Password = PwdRegConfirmText.Text ?? string.Empty;
        }

        // Start i skróty
        private void Window_Loaded(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;

        private void ToggleFullscreen()
        {
            if (_forceFullscreen)
            {
                _forceFullscreen = false;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }
            else
            {
                _forceFullscreen = true;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11) ToggleFullscreen();
            if (e.Key == Key.Escape) Close();
        }
    }
}








