using Finly.Services;
using Finly.ViewModels;
using Finly.Views;
using System;
using System.Globalization;
using System.Linq;
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
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { okBtn } });

            okBtn.Click += (_, __) =>
            {
                if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v >= 0)
                {
                    var uid = UserService.GetCurrentUserId();
                    DatabaseService.SetCashOnHand(uid, v);
                    (Window.GetWindow(okBtn))?.Close();
                    RefreshMoney();
                }
                else
                {
                    MessageBox.Show("Podaj poprawną kwotę ≥ 0.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            return panel;
        }

        private void WithdrawFromBank_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            var accounts = DatabaseService.GetAccounts(uid);
            if (accounts == null || accounts.Count == 0)
            {
                MessageBox.Show("Nie masz żadnych rachunków bankowych.", "Wypłata z banku",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var cmb = new ComboBox { Width = 340, Margin = new Thickness(0, 0, 0, 8) };
            var items = accounts.Select(a => new
            {
                a.Id,
                Label = $"{a.AccountName}  |  saldo: {a.Balance:N2} {a.Currency}"
            }).ToList();
            cmb.ItemsSource = items;
            cmb.DisplayMemberPath = "Label";
            cmb.SelectedIndex = 0;

            var amountBox = new TextBox { Width = 200, Margin = new Thickness(0, 0, 0, 8) };

            var dlg = new Window
            {
                Title = "Wypłata z banku do gotówki",
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Children =
                    {
                        new TextBlock{ Text="Wybierz rachunek:" },
                        cmb,
                        new TextBlock{ Text="Kwota (zł):" },
                        amountBox,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children =
                            {
                                new Button{ Content="OK", IsDefault=true, Width=100, Margin=new Thickness(0,8,8,0) },
                                new Button{ Content="Anuluj", IsCancel=true, Width=100, Margin=new Thickness(0,8,0,0) }
                            }
                        }
                    }
                }
            };

            // OK
            ((StackPanel)((StackPanel)dlg.Content).Children[4]).Children[0].AddHandler(Button.ClickEvent,
                new RoutedEventHandler((_, __) =>
                {
                    if (cmb.SelectedItem == null)
                    {
                        MessageBox.Show("Wybierz rachunek.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!decimal.TryParse(amountBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var amt) || amt <= 0)
                    {
                        MessageBox.Show("Podaj poprawną dodatnią kwotę.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var sel = (dynamic)cmb.SelectedItem;
                    try
                    {
                        DatabaseService.TransferBankToCash(uid, (int)sel.Id, amt);
                        dlg.Close();
                        RefreshMoney();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Nie udało się wykonać wypłaty", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }));

            dlg.ShowDialog();
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


