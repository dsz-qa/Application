using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Finly.Models;
using Finly.Services;
using Finly.Views.Dialogs;

namespace Finly.Pages
{
    public partial class AccountsPage : UserControl
    {
        private readonly int _userId;

        public AccountsPage() : this(SafeUserId()) { }

        public AccountsPage(int userId)
        {
            InitializeComponent();
            _userId = userId;
            Reload();
        }

        private static int SafeUserId()
        {
            try { return UserService.GetCurrentUserId(); } catch { return 0; }
        }

        private void Reload()
        {
            Grid.ItemsSource = DatabaseService.GetAccounts(_userId);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new EditAccountDialog(new BankAccountModel { UserId = _userId });
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true)
            {
                DatabaseService.InsertAccount(dlg.Model);
                ToastService.Success("Dodano konto.");
                Reload();
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is BankAccountModel sel)
            {
                var clone = new BankAccountModel
                {
                    Id = sel.Id,
                    UserId = sel.UserId,
                    ConnectionId = sel.ConnectionId,
                    AccountName = sel.AccountName,
                    Iban = sel.Iban,
                    Currency = sel.Currency,
                    Balance = sel.Balance
                };
                var dlg = new EditAccountDialog(clone) { Owner = Window.GetWindow(this) };
                if (dlg.ShowDialog() == true)
                {
                    DatabaseService.UpdateAccount(dlg.Model);
                    ToastService.Success("Zaktualizowano konto.");
                    Reload();
                }
            }
            else ToastService.Info("Wybierz konto z listy.");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is BankAccountModel sel)
            {
                var confirm = new Views.Dialogs.ConfirmDialog("Usunąć konto? (Uwaga: powiązane transakcje nie znikną)")
                { Owner = Window.GetWindow(this) };
                if (confirm.ShowDialog() == true && confirm.Result)
                {
                    DatabaseService.DeleteAccount(sel.Id, _userId);
                    ToastService.Success("Usunięto konto.");
                    Reload();
                }
            }
            else ToastService.Info("Wybierz konto z listy.");
        }
    }
}
