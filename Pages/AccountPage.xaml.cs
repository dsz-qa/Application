using Finly.Services;
using Finly.ViewModels;
using Finly.Views;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        }

        private void RefreshMoney()
        {
            var uid = Finly.Services.UserService.GetCurrentUserId();
            var s = DatabaseService.GetMoneySnapshot(uid);

            LblBanks.Text = s.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblCash.Text = s.Cash.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblEnvelopes.Text = s.Envelopes.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblAvailable.Text = s.AvailableToAllocate.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            RefreshMoney();
        }

        private void OpenEnvelopes_Click(object sender, RoutedEventArgs e)
        {
            // jeśli masz nawigację w ShellWindow:
            (Application.Current.MainWindow as Finly.Views.ShellWindow)?.NavigateTo("envelopes");
        }

        private void EditCash_Click(object sender, RoutedEventArgs e)
        {
            var uid = Finly.Services.UserService.GetCurrentUserId();
            var current = DatabaseService.GetCashOnHand(uid);

            var win = new Window
            {
                Title = "Ustaw gotówkę",
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Children =
            {
                new TextBlock{ Text="Stan gotówki (zł):" },
                new TextBox{ Name="Input", Width=200, Text=current.ToString(CultureInfo.CurrentCulture), Margin=new Thickness(0,6,0,12) },
                new StackPanel
                {
                    Orientation=Orientation.Horizontal,
                    Children =
                    {
                        new Button{ Content="OK", Width=80, IsDefault=true, Margin=new Thickness(0,0,8,0),
                            Command = new RoutedCommand() }
                    }
                }
            }
                }
            };

            // prosto: pobierz textbox przez VisualTree (tu krótsza droga)
            win.Loaded += (_, __) =>
            {
                var panel = (StackPanel)win.Content;
                var tb = (TextBox)panel.Children[1];
                var btnOk = ((StackPanel)panel.Children[2]).Children[0] as Button;
                btnOk.Click += (_, __2) =>
                {
                    if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v >= 0)
                    {
                        DatabaseService.SetCashOnHand(uid, v);
                        win.DialogResult = true;
                        win.Close();
                        RefreshMoney();
                    }
                    else
                    {
                        MessageBox.Show("Podaj poprawną kwotę ≥ 0.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };
            };

            win.ShowDialog();
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
