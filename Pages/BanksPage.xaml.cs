using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Finly.Pages
{
    public partial class BanksPage : UserControl
    {
        private int _uid => UserService.GetCurrentUserId();

        private readonly ObservableCollection<BankAccountVm> _accounts = new();
        private readonly ObservableCollection<object> _cards = new();
        private readonly AddAccountTile _addTile = new();
        private sealed class EnvelopeItem
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Allocated { get; set; }
            public override string ToString() => Name;
        }

        public BanksPage()
        {
            InitializeComponent();
            AccountsRepeater.ItemsSource = _cards;
            Loaded += BanksPage_Loaded;
        }

        private void BanksPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_uid <= 0) return;

            RefreshMoney();
            LoadAccounts();
        }

        private void RefreshMoney()
        {
            if (_uid <= 0) return;

            var snapshot = DatabaseService.GetMoneySnapshot(_uid);
            LblTotalBanks.Text = snapshot.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void LoadAccounts()
        {
            _accounts.Clear();
            _cards.Clear();

            List<BankAccountModel> list =
                DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();

            foreach (var acc in list)
            {
                var vm = new BankAccountVm(acc);
                _accounts.Add(vm);
                _cards.Add(vm);
            }

            _cards.Add(_addTile);
        }

        private static BankAccountVm? GetVmFromSender(object? sender)
        {
            if (sender is FrameworkElement fe)
            {
                if (fe.Tag is BankAccountVm vm1) return vm1;
                if (fe.DataContext is BankAccountVm vm2) return vm2;
            }
            return null;
        }

        private static T? FindInTree<T>(FrameworkElement root, string name)
            where T : FrameworkElement
        {
            if (root is T t && t.Name == name)
                return t;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(root, i) is FrameworkElement child)
                {
                    var result = FindInTree<T>(child, name);
                    if (result != null) return result;
                }
            }
            return null;
        }

        private void ResetAllActionButtons()
        {
            foreach (var item in AccountsRepeater.Items)
            {
                var container =
                    AccountsRepeater.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                if (container == null) continue;

                var withdrawBtn = FindInTree<Button>(container, "WithdrawButton");
                var depositBtn = FindInTree<Button>(container, "DepositButton");

                SetActionButtonState(withdrawBtn, false);
                SetActionButtonState(depositBtn, false);
            }
        }

        private static void SetActionButtonState(Button? btn, bool active)
        {
            if (btn == null) return;

            if (active)
            {
                btn.BorderThickness = new Thickness(1.5);
                btn.BorderBrush =
                    (Brush)Application.Current.TryFindResource("Brand.Orange") ?? Brushes.Orange;
            }
            else
            {
                btn.ClearValue(Button.BorderThicknessProperty);
                btn.ClearValue(Button.BorderBrushProperty);
            }
        }

        // ===== KOPERTY =====
        private List<EnvelopeItem> LoadEnvelopes()
        {
            var list = new List<EnvelopeItem>();
            if (_uid <= 0) return list;

            var dt = DatabaseService.GetEnvelopesTable(_uid);
            foreach (DataRow row in dt.Rows)
            {
                list.Add(new EnvelopeItem
                {
                    Id = Convert.ToInt32(row["Id"]),
                    Name = (row["Name"]?.ToString() ?? "(bez nazwy)").Trim(),
                    Allocated = row["Allocated"] == DBNull.Value ? 0m : Convert.ToDecimal(row["Allocated"])
                });
            }
            return list;
        }

        // ===== DODAJ / EDYTUJ =====

        private void AddAccountCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var model = new BankAccountModel
            {
                Id = 0,
                UserId = _uid,
                ConnectionId = 0,
                BankName = "Inny bank",
                AccountName = "",
                Iban = "",
                Currency = "PLN",
                Balance = 0m
            };

            OpenEditDialog(model, isNew: true);
        }

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
                AccountName = vm.AccountName == "(bez nazwy)" ? "" : vm.AccountName,
                Iban = vm.Iban,
                Currency = vm.Currency,
                Balance = vm.Balance
            };

            OpenEditDialog(model, isNew: false);
        }

        private void OpenEditDialog(BankAccountModel model, bool isNew)
        {
            try
            {
                var dlg = new EditBankAccountDialog
                {
                    Owner = Window.GetWindow(this)
                };

                dlg.SetMode(isNew ? EditBankAccountDialog.DialogMode.Add : EditBankAccountDialog.DialogMode.Edit);
                dlg.Load(model);

                if (dlg.ShowDialog() != true)
                    return;

                var entity = ToEntity(dlg.Result);

                if (entity.Id == 0)
                {
                    entity.Id = DatabaseService.InsertAccount(entity);
                    ToastService.Success("Dodano rachunek.");
                }
                else
                {
                    DatabaseService.UpdateAccount(entity);
                    ToastService.Success("Zaktualizowano rachunek.");
                }

                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać zmian: " + ex.Message);
            }
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
            Balance = m.Balance,
            LastSync = m.LastSync
        };

        // ===== WYPŁATA / WPŁATA =====

        private void WithdrawFromAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            if (sender is Button btn)
            {
                ResetAllActionButtons();
                SetActionButtonState(btn, true);
            }

            OpenOperationDialog(vm, BankOperationDialog.OperationKind.Withdraw);

            ResetAllActionButtons();
        }

        private void DepositToAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            if (sender is Button btn)
            {
                ResetAllActionButtons();
                SetActionButtonState(btn, true);
            }

            OpenOperationDialog(vm, BankOperationDialog.OperationKind.Deposit);

            ResetAllActionButtons();
        }

        private void OpenOperationDialog(BankAccountVm vm, BankOperationDialog.OperationKind kind)
        {
            try
            {
                var dlg = new BankOperationDialog
                {
                    Owner = Window.GetWindow(this)
                };

                dlg.Configure(kind, vm.AccountName);

                var envelopes = LoadEnvelopes();
                dlg.SetEnvelopes(envelopes);

                if (dlg.ShowDialog() != true)
                    return;

                ExecuteBankOperation(vm.Id, dlg);
                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się wykonać operacji: " + ex.Message);
            }
        }

        private void ExecuteBankOperation(int bankAccountId, BankOperationDialog dlg)
        {
            var amount = dlg.Amount;
            var source = dlg.SourceKind;

            if (dlg.Kind == BankOperationDialog.OperationKind.Withdraw)
            {
                switch (source)
                {
                    case "free":
                        TransactionsFacadeService.TransferBankToFreeCash(_uid, bankAccountId, amount);
                        ToastService.Success($"Wypłacono {amount:N2} zł do wolnej gotówki.");
                        return;

                    case "saved":
                        TransactionsFacadeService.TransferBankToSaved(_uid, bankAccountId, amount);
                        ToastService.Success($"Wypłacono {amount:N2} zł do odłożonej gotówki.");
                        return;

                    case "envelope":
                        if (dlg.SelectedEnvelopeId == null)
                            throw new InvalidOperationException("Nie wybrano koperty.");

                        TransactionsFacadeService.TransferBankToEnvelope(_uid, bankAccountId, dlg.SelectedEnvelopeId.Value, amount);
                        ToastService.Success($"Przelano {amount:N2} zł z konta do koperty.");
                        return;
                }
            }
            else
            {
                switch (source)
                {
                    case "free":
                        TransactionsFacadeService.TransferFreeCashToBank(_uid, bankAccountId, amount);
                        ToastService.Success($"Wpłacono {amount:N2} zł na konto.");
                        return;

                    case "saved":
                        TransactionsFacadeService.TransferSavedToBank(_uid, bankAccountId, amount);
                        ToastService.Success($"Wpłacono {amount:N2} zł z odłożonej gotówki na konto.");
                        return;

                    case "envelope":
                        if (dlg.SelectedEnvelopeId == null)
                            throw new InvalidOperationException("Nie wybrano koperty.");

                        TransactionsFacadeService.TransferEnvelopeToBank(_uid, dlg.SelectedEnvelopeId.Value, bankAccountId, amount);
                        ToastService.Success($"Wpłacono {amount:N2} zł z koperty na konto.");
                        return;
                }
            }

            throw new InvalidOperationException("Nieznany typ operacji.");
        }

        // ===== USUWANIE =====

        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            if (sender is not FrameworkElement fe)
                return;

            HideAllDeletePanels();

            FrameworkElement? container = fe;
            while (container != null &&
                   container is not ContentPresenter &&
                   container is not Border)
            {
                container = VisualTreeHelper.GetParent(container) as FrameworkElement;
            }

            if (container == null) return;

            var panel = FindInTree<Border>(container, "DeleteConfirmPanel");
            if (panel == null) return;

            panel.Visibility = panel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void HideAllDeletePanels()
        {
            foreach (var item in AccountsRepeater.Items)
            {
                var container =
                    AccountsRepeater.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                if (container == null) continue;

                var panel = FindInTree<Border>(container, "DeleteConfirmPanel");
                if (panel != null)
                    panel.Visibility = Visibility.Collapsed;
            }
        }

        private void DeleteCancel_Click(object sender, RoutedEventArgs e)
        {
            HideAllDeletePanels();
        }

        private void DeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                HideAllDeletePanels();
                return;
            }

            try
            {
                DatabaseService.DeleteAccount(vm.Id, _uid);
                ToastService.Success("Usunięto rachunek.");
                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd usuwania rachunku: " + ex.Message);
            }
            finally
            {
                HideAllDeletePanels();
            }
        }
    }

    // ===== VIEWMODEL KAFELKA KONTA =====
    public sealed class BankAccountVm
    {
        public int Id { get; }
        public string BankName { get; }
        public string AccountName { get; }
        public string Iban { get; }
        public string Currency { get; }
        public decimal Balance { get; }

        public string LogoPath { get; }

        public string BalanceStr => Balance.ToString("N2", CultureInfo.CurrentCulture) + " zł";

        public BankAccountVm(BankAccountModel m)
        {
            Id = m.Id;
            BankName = m.BankName ?? "";
            AccountName = string.IsNullOrWhiteSpace(m.AccountName) ? "(bez nazwy)" : m.AccountName;
            Iban = m.Iban ?? "";
            Currency = string.IsNullOrWhiteSpace(m.Currency) ? "PLN" : m.Currency;
            Balance = m.Balance;

            LogoPath = GetLogoForBank(BankName);
        }

        private static string GetLogoForBank(string? bankName)
        {
            var text = (bankName ?? "").ToLowerInvariant();

            if (text.Contains("revolut"))
                return "pack://application:,,,/Assets/Banks/revolutlogo.png";
            if (text.Contains("mbank"))
                return "pack://application:,,,/Assets/Banks/mbanklogo.jpg";
            if (text.Contains("pko "))
                return "pack://application:,,,/Assets/Banks/pkobplogo.jpg";
            if (text.Contains("pekao"))
                return "pack://application:,,,/Assets/Banks/pekaologo.jpg";
            if (text.Contains("ing"))
                return "pack://application:,,,/Assets/Banks/inglogo.png";
            if (text.Contains("credit agricole") || text.Contains("creditagricole"))
                return "pack://application:,,,/Assets/Banks/creditagricolelogo.png";
            if (text.Contains("santander"))
                return "pack://application:,,,/Assets/Banks/santanderlogo.png";
            if (text.Contains("alior"))
                return "pack://application:,,,/Assets/Banks/aliorbanklogo.png";
            if (text.Contains("millennium"))
                return "pack://application:,,,/Assets/Banks/milleniumlogo.png";

            return "pack://application:,,,/Assets/Banks/innybank.png";
        }
    }
    public sealed class AddAccountTile { }
}
