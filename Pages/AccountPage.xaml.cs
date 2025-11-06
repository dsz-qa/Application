using Finly.Services;
using Finly.ViewModels;
using Finly.Views;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class AccountPage : UserControl
    {
        private readonly int _userId;
        private AccountViewModel _vm;

        public AccountPage(int userId)
        {
            InitializeComponent();
            _userId = userId;
            _vm = new AccountViewModel(userId);
            DataContext = _vm;
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
                if (ok)
                    vm.Refresh();     // <- po zapisie odśwież widok: e-mail, adres, itp.
            }
        }


        // PRZYCISK: „Zmień hasło”
        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AccountViewModel vm) return;

            // PwdOld / PwdNew / PwdNew2 masz nazwane w XAML
            var oldPwd = PwdOld.Password;
            var newPwd = PwdNew.Password;
            var newPwd2 = PwdNew2.Password;

            vm.ChangePassword(oldPwd, newPwd, newPwd2);

            // Opcjonalnie czyścimy pola:
            // PwdOld.Password = PwdNew.Password = PwdNew2.Password = string.Empty;
        }
    }
}
