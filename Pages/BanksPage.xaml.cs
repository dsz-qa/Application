using Finly.Models;
using Finly.Services;
using Finly.Views.Dialogs;
using System;
using System.Collections.ObjectModel;
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

        // Kolekcja podpięta do ItemsControl (kafelki)
        private readonly ObservableCollection<BankAccountVm> _accounts = new();

        public BanksPage()
        {
            InitializeComponent();
            AccountsRepeater.ItemsSource = _accounts;
            Loaded += BanksPage_Loaded;
        }

        private void BanksPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshMoney();
            LoadAccounts();
        }

        // === GÓRNY KAFEL "RAZEM WSZYSTKIE KONTA" ===
        private void RefreshMoney()
        {
            var snapshot = DatabaseService.GetMoneySnapshot(_uid);
            LblTotalBanks.Text =
                snapshot.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        // === Wczytywanie kont do kafelków ===
        private void LoadAccounts()
        {
            _accounts.Clear();

            var list = DatabaseService.GetAccounts(_uid)
                       ?? new System.Collections.Generic.List<BankAccountModel>();

            foreach (var acc in list)
                _accounts.Add(new BankAccountVm(acc));
        }

        // Pomocnik – wyciągnięcie VM z przycisku (Tag albo DataContext)
        private static BankAccountVm? GetVmFromSender(object? sender)
        {
            if (sender is FrameworkElement fe)
            {
                if (fe.Tag is BankAccountVm vm1) return vm1;
                if (fe.DataContext is BankAccountVm vm2) return vm2;
            }
            return null;
        }

        // ================== DIALOG Z KWOTĄ ==================
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
                Background = (Brush)Application.Current.TryFindResource("Surface.Field")
                            ?? new SolidColorBrush(Color.FromRgb(40, 40, 40)),
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
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btns.Children.Add(ok);
            btns.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(lbl);
            root.Children.Add(tb);
            root.Children.Add(btns);

            win.Content = root;

            if (win.ShowDialog() == true)
            {
                var txt = (tb.Text ?? "").Replace(" ", "");
                if (decimal.TryParse(txt, NumberStyles.Number,
                    CultureInfo.CurrentCulture, out var v) && v > 0)
                    return v;
            }
            return null;
        }

        // ================== OPERACJE NA KONCIE (z kafelka) ==================

        // Wypłata z konta do gotówki (na razie logika jak wcześniej – do wolnej gotówki)
        private void WithdrawFromAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            var owner = Window.GetWindow(this)!;
            var amount = PromptAmount(owner, $"Wypłata z konta \"{vm.AccountName}\"");
            if (amount == null) return;

            try
            {
                DatabaseService.TransferBankToCash(_uid, vm.Id, amount.Value);
                ToastService.Success($"Wypłacono {amount.Value:N2} zł do gotówki.");
                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się wykonać wypłaty: " + ex.Message);
            }
        }

        // Wpłata z gotówki na konto
        private void DepositToAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            var owner = Window.GetWindow(this)!;
            var amount = PromptAmount(owner, $"Wpłata na konto \"{vm.AccountName}\"");
            if (amount == null) return;

            try
            {
                DatabaseService.TransferCashToBank(_uid, vm.Id, amount.Value);
                ToastService.Success($"Wpłacono {amount.Value:N2} zł na konto.");
                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się wykonać wpłaty: " + ex.Message);
            }
        }

        // Edycja konta (nazwa, IBAN itd.)
        private void EditAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            var model = new BankAccountModel
            {
                Id = vm.Id,
                UserId = _uid,
                ConnectionId = 0,
                BankName = vm.BankName,
                AccountName = vm.AccountName,
                Iban = vm.Iban,
                Currency = vm.Currency,
                Balance = vm.Balance
            };

            var dlg = new EditAccountDialog(model) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                var entity = ToEntity(dlg.Model);
                DatabaseService.UpdateAccount(entity);
                ToastService.Success("Zaktualizowano rachunek.");
                LoadAccounts();
                RefreshMoney();
            }
        }

        // Usuwanie konta
        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            var c = new ConfirmDialog("Usunąć ten rachunek? Operacja nieodwracalna.")
            { Owner = Window.GetWindow(this) };

            if (c.ShowDialog() == true && c.Result)
            {
                DatabaseService.DeleteAccount(vm.Id, _uid);
                ToastService.Success("Usunięto rachunek.");
                LoadAccounts();
                RefreshMoney();
            }
        }

        // Dodawanie konta z przycisku "Dodaj konto"
        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            var model = new BankAccountModel
            {
                UserId = _uid,
                Currency = "PLN",
                Balance = 0m
            };

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

        // Mapowanie modelu z dialogu na encję pod bazę
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
    }

    // ===== ViewModel kafelka =====
    public sealed class BankAccountVm
    {
        public int Id { get; }
        public string BankName { get; }
        public string AccountName { get; }
        public string Iban { get; }
        public string Currency { get; }
        public decimal Balance { get; }

        public string BalanceStr =>
            Balance.ToString("N2", CultureInfo.CurrentCulture) + " zł";

        // Ścieżka do logo banku (pack URI)
        public string LogoPath { get; }

        public BankAccountVm(BankAccountModel m)
        {
            Id = m.Id;
            BankName = m.BankName ?? "";
            AccountName = string.IsNullOrWhiteSpace(m.AccountName) ? "(bez nazwy)" : m.AccountName;
            Iban = m.Iban ?? "";
            Currency = string.IsNullOrWhiteSpace(m.Currency) ? "PLN" : m.Currency;
            Balance = m.Balance;

            LogoPath = GetLogoPath(BankName);
        }

        private static string GetLogoPath(string bankName)
        {
            var n = (bankName ?? "").ToLowerInvariant();

            if (n.Contains("mbank")) return "pack://application:,,,/Assets/Banks/mbanklogo.jpg";
            if (n.Contains("pko") && n.Contains("polski")) return "pack://application:,,,/Assets/Banks/pkobplogo.jpg";
            if (n.Contains("pekao")) return "pack://application:,,,/Assets/Banks/pekaologo.jpg";
            if (n.Contains("ing")) return "pack://application:,,,/Assets/Banks/inglogo.png";
            if (n.Contains("santander")) return "pack://application:,,,/Assets/Banks/santanderlogo.png";
            if (n.Contains("alior")) return "pack://application:,,,/Assets/Banks/aliorbanklogo.png";
            if (n.Contains("millennium")) return "pack://application:,,,/Assets/Banks/milleniumlogo.png";
            if (n.Contains("credit") && n.Contains("agricole"))
                return "pack://application:,,,/Assets/Banks/creditagricolelogo.png";
            if (n.Contains("revolut")) return "pack://application:,,,/Assets/Banks/revolutlogo.png";

            // domyślne logo „inny bank”
            return "pack://application:,,,/Assets/Banks/innybank.png";
        }
    }
}







