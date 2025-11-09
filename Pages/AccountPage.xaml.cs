using Finly.Services;
using Finly.ViewModels;
using Finly.Views;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class AccountPage : UserControl
    {
        private readonly int _userId;
        private readonly AccountViewModel _vm;

        public AccountPage(int userId)
        {
            InitializeComponent();
            _userId = userId;
            _vm = new AccountViewModel(userId);
            DataContext = _vm;

            // Odśwież KPI, gdy widok jest już w wizualnym drzewie
            Loaded += (_, __) => RefreshMoney();
        }

        private void RefreshMoney()
        {
            var uid = UserService.GetCurrentUserId();
            var s = DatabaseService.GetMoneySnapshot(uid);

            LblBanks.Text = s.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblCash.Text = s.Cash.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblEnvelopes.Text = s.Envelopes.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblAvailable.Text = s.AvailableToAllocate.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void OpenEnvelopes_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current.MainWindow as ShellWindow)?.NavigateTo("envelopes");
        }

        private void EditCash_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            var current = DatabaseService.GetCashOnHand(uid);

            var win = new Window
            {
                Title = "Ustaw gotówkę",
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = BuildCashEditor(current)
            };

            win.ShowDialog();
        }

        private UIElement BuildCashEditor(decimal current)
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            var tb = new TextBox { Width = 200, Text = current.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 6, 0, 12) };
            var okBtn = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };

            panel.Children.Add(new TextBlock { Text = "Stan gotówki (zł):" });
            panel.Children.Add(tb);
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { okBtn }
            });

            okBtn.Click += (_, __) =>
            {
                if (decimal.TryParse(tb.Text, System.Globalization.NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v >= 0)
                {
                    var uid = UserService.GetCurrentUserId();
                    DatabaseService.SetCashOnHand(uid, v);

                    // Zamknij okno i odśwież KPI
                    var win = Window.GetWindow(okBtn);
                    if (win != null) { win.DialogResult = true; win.Close(); }
                    RefreshMoney();
                }
                else
                {
                    MessageBox.Show("Podaj poprawną kwotę ≥ 0.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            return panel;
        }

        private void EditPersonal_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AccountViewModel vm)
            {
                var dlg = new EditPersonalWindow(UserService.GetCurrentUserId())
                {
                    Owner = Application.Current.MainWindow
                };
                var ok = dlg.ShowDialog() == true;
                if (ok) vm.Refresh();
            }
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AccountViewModel vm) return;

            var oldPwd = PwdOld.Password;
            var newPwd = PwdNew.Password;
            var newPwd2 = PwdNew2.Password;

            vm.ChangePassword(oldPwd, newPwd, newPwd2);
        }

        private void OpenBanks_Click(object sender, RoutedEventArgs e)
        {
            (Window.GetWindow(this) as ShellWindow)?.NavigateTo("banks");
        }
    }
}

