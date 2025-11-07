using System;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Finly.Models;
using Finly.Services;
using Finly.Views.Dialogs;

namespace Finly.Pages
{
    public partial class BanksPage : UserControl
    {
        private int _uid => UserService.GetCurrentUserId();

        public BanksPage()
        {
            InitializeComponent();
            LoadAccounts();
        }

        private void LoadAccounts()
        {
            var dt = DatabaseService.GetAccountsTable(_uid);
            AccountsGrid.ItemsSource = dt.DefaultView;
        }

        private static int? TryGetIdFromTag(object tag)
        {
            if (tag == null) return null;
            if (tag is int i) return i;
            return int.TryParse(tag.ToString(), out var parsed) ? parsed : (int?)null;
        }

        private BankAccountModel? BuildModelFromRowId(int id)
        {
            if (AccountsGrid.ItemsSource is not DataView dv) return null;

            var row = dv.Table.AsEnumerable().FirstOrDefault(r =>
                (r.Table.Columns["Id"].DataType == typeof(long) && Convert.ToInt64(r["Id"]) == id) ||
                (r.Table.Columns["Id"].DataType == typeof(int) && Convert.ToInt32(r["Id"]) == id));

            if (row == null) return null;

            var bankName = row.Table.Columns.Contains("BankName") ? (row["BankName"]?.ToString() ?? "") : "";

            return new BankAccountModel
            {
                Id = Convert.ToInt32(row["Id"]),
                UserId = _uid,
                ConnectionId = 0,
                BankName = bankName,
                AccountName = row["AccountName"]?.ToString() ?? "",
                Iban = row["Iban"]?.ToString() ?? "",
                Currency = row["Currency"]?.ToString() ?? "PLN",
                Balance = Convert.ToDecimal(row["Balance"])
            };
        }

        private static BankAccountModel ToEntity(BankAccountModel m) => new BankAccountModel
        {
            Id = m.Id,
            UserId = m.UserId,
            ConnectionId = m.ConnectionId,
            BankName = m.BankName ?? "",
            AccountName = m.AccountName?.Trim() ?? "",
            Iban = m.Iban?.Trim() ?? "",
            Currency = string.IsNullOrWhiteSpace(m.Currency) ? "PLN" : m.Currency.Trim(),
            Balance = m.Balance
        };

        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            var model = new BankAccountModel { UserId = _uid, Currency = "PLN", Balance = 0m };
            var dlg = new EditAccountDialog(model) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                var entity = ToEntity(dlg.Model);
                entity.Id = DatabaseService.InsertAccount(entity);
                ToastService.Success("Dodano rachunek.");
                LoadAccounts();
            }
        }

        private void EditAccount_Click(object sender, RoutedEventArgs e)
        {
            var id = TryGetIdFromTag((sender as Button)?.Tag);
            if (id == null) { ToastService.Info("Nie udało się odczytać identyfikatora rachunku."); return; }

            var model = BuildModelFromRowId(id.Value);
            if (model == null) { ToastService.Info("Nie znaleziono rachunku."); return; }

            var dlg = new EditAccountDialog(model) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                DatabaseService.UpdateAccount(ToEntity(dlg.Model));
                ToastService.Success("Zaktualizowano rachunek.");
                LoadAccounts();
            }
        }

        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            var id = TryGetIdFromTag((sender as Button)?.Tag);
            if (id == null) { ToastService.Info("Nie udało się odczytać identyfikatora rachunku."); return; }

            var c = new ConfirmDialog("Usunąć ten rachunek? Operacja nieodwracalna.")
            { Owner = Window.GetWindow(this) };

            if (c.ShowDialog() == true && c.Result)
            {
                DatabaseService.DeleteAccount(id.Value, _uid);
                ToastService.Success("Usunięto rachunek.");
                LoadAccounts();
            }
        }

        private void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsGrid.SelectedItem is DataRowView drv)
                EditAccount_Click(new Button { Tag = Convert.ToInt32(drv.Row["Id"]) }, new RoutedEventArgs());
            else
                ToastService.Info("Zaznacz rachunek do edycji.");
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsGrid.SelectedItem is DataRowView drv)
                DeleteAccount_Click(new Button { Tag = Convert.ToInt32(drv.Row["Id"]) }, new RoutedEventArgs());
            else
                ToastService.Info("Zaznacz rachunek do usunięcia.");
        }

        private void AccountsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AccountsGrid.SelectedItem is DataRowView drv)
                EditAccount_Click(new Button { Tag = Convert.ToInt32(drv.Row["Id"]) }, new RoutedEventArgs());
        }
    }
}

