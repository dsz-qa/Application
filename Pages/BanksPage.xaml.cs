using Finly.Models;
using Finly.Services;
using Finly.Views.Dialogs;
using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Finly.Pages
{
    public partial class BanksPage : UserControl
    {
        private int _uid => UserService.GetCurrentUserId();

        public BanksPage()
        {
            InitializeComponent();
            RefreshMoney();
            LoadAccounts();
        }

        // === Snapshot ===
        private void RefreshMoney()
        {
            var s = DatabaseService.GetMoneySnapshot(_uid);
            LblBanks.Text = s.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblCash.Text = s.Cash.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            LblEnvelopes.Text = s.Envelopes.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            // tu wcześniej było s.AvailableToAllocate
            LblAvailable.Text = s.SavedUnallocated.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private static decimal? PromptAmount(Window owner, string title, string label = "Kwota (PLN):")
        {
            var win = new Window
            {
                Title = title,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = (Brush)Application.Current.TryFindResource("Surface.Background") ?? Brushes.Black,
                Foreground = (Brush)Application.Current.TryFindResource("Text.Primary") ?? Brushes.White
            };

            var lbl = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = win.Foreground
            };

            var tb = new TextBox
            {
                Width = 220,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 10),
                Background = (Brush)Application.Current.TryFindResource("Surface.Field") ?? new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = win.Foreground,
                BorderBrush = (Brush)Application.Current.TryFindResource("Surface.Border")
            };

            var ok = new Button
            {
                Content = "OK",
                Width = 100,
                IsDefault = true,
                Style = (Style)Application.Current.TryFindResource("PrimaryButton"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new Button
            {
                Content = "Anuluj",
                Width = 100,
                IsCancel = true
            };

            ok.Click += (_, __) => { win.DialogResult = true; };
            cancel.Click += (_, __) => { win.DialogResult = false; };

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { ok, cancel }
            };

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(lbl);
            root.Children.Add(tb);
            root.Children.Add(btns);

            win.Content = root;

            if (win.ShowDialog() == true)
            {
                var txt = (tb.Text ?? "").Replace(" ", "");
                if (decimal.TryParse(txt, NumberStyles.Number, CultureInfo.CurrentCulture, out var v) && v > 0)
                    return v;
            }
            return null;
        }

        // ===== WY/PŁATA =====

        private void WithdrawToCash_Click(object sender, RoutedEventArgs e)
        {
            var id = TryGetIdFromTag((sender as Button)?.Tag);
            if (id == null) { ToastService.Info("Nie udało się odczytać identyfikatora rachunku."); return; }

            var amount = PromptAmount(Window.GetWindow(this)!, "Wypłać z konta");
            if (amount == null) return;

            try
            {
                DatabaseService.TransferBankToCash(_uid, id.Value, amount.Value);
                ToastService.Success($"Wypłacono {amount.Value:N2} zł do gotówki.");
                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się wykonać wypłaty: " + ex.Message);
            }
        }

        private void DepositFromCash_Click(object sender, RoutedEventArgs e)
        {
            var id = TryGetIdFromTag((sender as Button)?.Tag);
            if (id == null) { ToastService.Info("Nie udało się odczytać identyfikatora rachunku."); return; }

            var amount = PromptAmount(Window.GetWindow(this)!, "Wpłać na konto");
            if (amount == null) return;

            try
            {
                DatabaseService.TransferCashToBank(_uid, id.Value, amount.Value);
                ToastService.Success($"Wpłacono {amount.Value:N2} zł na konto.");
                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się wykonać wpłaty: " + ex.Message);
            }
        }

        private void OpenEnvelopes_Click(object sender, RoutedEventArgs e)
            => (Window.GetWindow(this) as Finly.Views.ShellWindow)?.NavigateTo("envelopes");

        private void EditCash_Click(object sender, RoutedEventArgs e)
        {
            var current = DatabaseService.GetCashOnHand(_uid);
            var win = new Window
            {
                Title = "Ustaw gotówkę",
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = BuildCashEditor(current)
            };
            if (win.ShowDialog() == true) RefreshMoney();
        }

        private UIElement BuildCashEditor(decimal current)
        {
            var p = new StackPanel { Margin = new Thickness(16) };
            var tb = new TextBox { Width = 200, Text = current.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 6, 0, 12) };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true };

            p.Children.Add(new TextBlock { Text = "Stan gotówki (zł):" });
            p.Children.Add(tb);
            p.Children.Add(ok);

            ok.Click += (_, __) =>
            {
                if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v >= 0)
                {
                    DatabaseService.SetCashOnHand(_uid, v);
                    var w = Window.GetWindow(ok); if (w != null) { w.DialogResult = true; w.Close(); }
                }
                else MessageBox.Show("Podaj poprawną kwotę ≥ 0.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            return p;
        }

        private void WithdrawFromBank_Click(object sender, RoutedEventArgs e)
        {
            var accs = DatabaseService.GetAccounts(_uid);
            if (accs.Count == 0) { MessageBox.Show("Brak rachunków bankowych.", "Info"); return; }

            var cb = new ComboBox { Width = 280, ItemsSource = accs, DisplayMemberPath = "AccountName", SelectedIndex = 0 };
            var tb = new TextBox { Width = 140, HorizontalContentAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Rachunek:" });
            panel.Children.Add(cb);
            panel.Children.Add(new TextBlock { Text = "Kwota (zł):", Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(tb);

            var ok = new Button { Content = "Wypłać", Width = 100, Margin = new Thickness(0, 12, 0, 0), IsDefault = true };
            panel.Children.Add(ok);

            var win = new Window
            {
                Title = "Wypłata z konta",
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = panel
            };

            ok.Click += (_, __) =>
            {
                if (cb.SelectedItem is not BankAccountModel sel) return;
                if (!decimal.TryParse(tb.Text.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
                {
                    MessageBox.Show("Podaj poprawną dodatnią kwotę.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    DatabaseService.TransferBankToCash(_uid, sel.Id, amount);
                    win.DialogResult = true; win.Close();
                    LoadAccounts();
                    RefreshMoney();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            win.ShowDialog();
        }

        // === tabela rachunków ===
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

        private static BankAccountModel ToEntity(BankAccountModel m) => new()
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
                RefreshMoney();
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
                RefreshMoney();
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
                RefreshMoney();
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



