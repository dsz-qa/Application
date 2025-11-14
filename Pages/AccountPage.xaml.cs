using Finly.Models;
using Finly.Services;
using Finly.ViewModels;
using Finly.Views;              // ShellWindow, AuthWindow
using Finly.Views.Dialogs;      // ConfirmDialog
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

            Loaded += (_, __) =>
            {
                RefreshMoney();
                LoadAccountsIntoGrid();
            };
        }

        private void RefreshMoney()
        {
            var uid = UserService.GetCurrentUserId();
            var s = DatabaseService.GetMoneySnapshot(uid);

            void SetLabel(string name, decimal val)
            {
                if (FindName(name) is TextBlock tb)
                    tb.Text = val.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            }

            SetLabel("LblBanks", s.Banks);
            SetLabel("LblCash", s.Cash);
            SetLabel("LblEnvelopes", s.Envelopes);
            // tutaj wcześniej było s.AvailableToAllocate – zmienione na SavedUnallocated
            SetLabel("LblAvailable", s.SavedUnallocated);
        }

        private void OpenEnvelopes_Click(object sender, RoutedEventArgs e)
            => (Application.Current.MainWindow as ShellWindow)?.NavigateTo("envelopes");

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
            var tb = new TextBox
            {
                Width = 200,
                Text = current.ToString(CultureInfo.CurrentCulture),
                Margin = new Thickness(0, 6, 0, 12)
            };
            var okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };

            panel.Children.Add(new TextBlock { Text = "Stan gotówki (zł):" });
            panel.Children.Add(tb);
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { okBtn }
            });

            okBtn.Click += (_, __) =>
            {
                if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v >= 0)
                {
                    var uid = UserService.GetCurrentUserId();
                    DatabaseService.SetCashOnHand(uid, v);
                    Window.GetWindow(okBtn)?.Close();
                    RefreshMoney();
                }
                else
                {
                    MessageBox.Show("Podaj poprawną kwotę ≥ 0.", "Błąd",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
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
            => (Window.GetWindow(this) as ShellWindow)?.NavigateTo("banks");

        // ====== BANKI (grid) ======

        private void LoadAccountsIntoGrid()
        {
            if (DataContext is AccountViewModel vm)
                AccountsGrid.ItemsSource = vm.BankAccounts;
        }

        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            var model = new BankAccountModel
            {
                UserId = UserService.GetCurrentUserId(),
                Currency = "PLN",
                Balance = 0m
            };

            var dlg = new EditAccountDialog(model) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                var entity = dlg.Model;
                entity.Id = DatabaseService.InsertAccount(entity);
                ToastService.Success("Dodano rachunek.");
                if (DataContext is AccountViewModel vm) vm.Refresh();
                LoadAccountsIntoGrid();
                RefreshMoney();
            }
        }

        private void EditAccount_Click(object sender, RoutedEventArgs e)
        {
            var id = (sender as Button)?.Tag is int i ? i : 0;
            if (id <= 0)
            {
                ToastService.Info("Nie udało się odczytać identyfikatora rachunku.");
                return;
            }

            var src = AccountsGrid.ItemsSource as System.Collections.IEnumerable;
            var item = src?.OfType<BankAccountModel>().FirstOrDefault(a => a.Id == id);
            if (item == null)
            {
                ToastService.Info("Nie znaleziono rachunku.");
                return;
            }

            var dlg = new EditAccountDialog(item) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                DatabaseService.UpdateAccount(dlg.Model);
                ToastService.Success("Zaktualizowano rachunek.");
                if (DataContext is AccountViewModel vm) vm.Refresh();
                LoadAccountsIntoGrid();
                RefreshMoney();
            }
        }

        private void DeleteBankAccount_Click(object sender, RoutedEventArgs e)
        {
            var id = (sender as Button)?.Tag is int i ? i : 0;
            if (id <= 0)
            {
                ToastService.Info("Nie udało się odczytać identyfikatora rachunku.");
                return;
            }

            var c = new ConfirmDialog("Usunąć ten rachunek? Operacja nieodwracalna.")
            {
                Owner = Window.GetWindow(this)
            };

            if (c.ShowDialog() == true && c.Result)
            {
                DatabaseService.DeleteAccount(id, UserService.GetCurrentUserId());
                ToastService.Success("Usunięto rachunek.");
                if (DataContext is AccountViewModel vm) vm.Refresh();
                LoadAccountsIntoGrid();
                RefreshMoney();
            }
        }

        private void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsGrid.SelectedItem is BankAccountModel m)
                EditAccount_Click(new Button { Tag = m.Id }, new RoutedEventArgs());
            else
                ToastService.Info("Zaznacz rachunek do edycji.");
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsGrid.SelectedItem is BankAccountModel m)
                DeleteBankAccount_Click(new Button { Tag = m.Id }, new RoutedEventArgs());
            else
                ToastService.Info("Zaznacz rachunek do usunięcia.");
        }

        private void AccountsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AccountsGrid.SelectedItem is BankAccountModel m)
                EditAccount_Click(new Button { Tag = m.Id }, new RoutedEventArgs());
        }

        // ===== USUWANIE KONTA UŻYTKOWNIKA (profilu) =====
        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            var dlg = new ConfirmDialog(
                "Usunąć konto użytkownika?\n\n" +
                "Tej operacji nie można cofnąć. Zostaną usunięte wszystkie Twoje dane: " +
                "rachunki, transakcje, budżety, kategorie itp.")
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true && dlg.Result)
            {
                try
                {
                    DatabaseService.DeleteUserCascade(uid);
                    UserService.ClearCurrentUser();
                    ToastService.Success("Twoje konto zostało usunięte.");

                    var auth = new AuthWindow();
                    Application.Current.MainWindow = auth;
                    auth.Show();

                    Window.GetWindow(this)?.Close();
                }
                catch (Exception ex)
                {
                    ToastService.Error("Nie udało się usunąć konta: " + ex.Message);
                }
            }
        }
    }
}








