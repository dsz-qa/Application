using Finly.Models;
using Finly.Services;
using Finly.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        // same konta
        private readonly ObservableCollection<BankAccountVm> _accounts = new();

        // to, co wyświetla ItemsControl (konta + kafel Dodaj)
        private readonly ObservableCollection<object> _cards = new();

        private readonly AddAccountTile _addTile = new();

        private enum BankOperationKind
        {
            None,
            Withdraw,
            Deposit
        }

        private BankOperationKind _currentOperation = BankOperationKind.None;
        private BankAccountVm? _currentAccount;

        private BankAccountModel? _editModel;

        public BanksPage()
        {
            InitializeComponent();

            AccountsRepeater.ItemsSource = _cards;
            Loaded += BanksPage_Loaded;

            EditBankCombo.SelectionChanged += EditBankCombo_SelectionChanged;
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

            // na końcu kafel Dodaj konto
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

        // ===== helpers do drzewka wizualnego =====

        private static T? FindInTree<T>(FrameworkElement root, string name)
            where T : FrameworkElement
        {
            if (root is T t && root.Name == name)
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

        // ===== POMOCNICZE – wyciąganie nazwy banku z ComboBoxItem (string albo StackPanel) =====
        private static string ExtractBankName(object? item)
        {
            if (item is ComboBoxItem ci)
            {
                // 1) Prosty przypadek – Content to string
                if (ci.Content is string s)
                    return s.Trim();

                // 2) Nasz StackPanel: [Image][TextBlock]
                if (ci.Content is StackPanel sp)
                {
                    foreach (var child in sp.Children)
                    {
                        if (child is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                            return tb.Text.Trim();
                    }
                }
            }
            return "";
        }

        private string GetSelectedEditBankName()
        {
            return ExtractBankName(EditBankCombo.SelectedItem);
        }

        // ===== PANEL OPERACJI (WYPŁATA / WPŁATA) =====

        private void ShowOperationPanel(BankOperationKind kind, BankAccountVm vm, FrameworkElement clickedButton)
        {
            HideEditPanel();

            _currentOperation = kind;
            _currentAccount = vm;

            if (kind == BankOperationKind.Withdraw)
            {
                OpTitle.Text = $"Wypłata z konta \"{vm.AccountName}\"";
                OpMainLabel.Text = "Do:";
                OpEnvelopeLabel.Text = "Do koperty:";
            }
            else
            {
                OpTitle.Text = $"Wpłata na konto \"{vm.AccountName}\"";
                OpMainLabel.Text = "Z:";
                OpEnvelopeLabel.Text = "Z koperty:";
            }

            OpAmountBox.Text = "";
            OpErrorText.Text = "";
            OpErrorText.Visibility = Visibility.Collapsed;

            OpFromCashRadio.IsChecked = true;
            OpFromEnvelopeRadio.IsChecked = false;
            OpCashCombo.SelectedIndex = 0;
            OpEnvelopeCombo.SelectedIndex = 0;

            OperationPanel.Visibility = Visibility.Visible;
            EditPanel.Visibility = Visibility.Collapsed;

            ResetAllActionButtons();
            if (clickedButton.Name == "WithdrawButton")
                SetActionButtonState(clickedButton as Button, true);
            else if (clickedButton.Name == "DepositButton")
                SetActionButtonState(clickedButton as Button, true);
        }

        private void HideOperationPanel()
        {
            OperationPanel.Visibility = Visibility.Collapsed;
            _currentOperation = BankOperationKind.None;
            _currentAccount = null;
            ResetAllActionButtons();
        }

        private void WithdrawFromAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            if (sender is FrameworkElement fe)
                ShowOperationPanel(BankOperationKind.Withdraw, vm, fe);
        }

        private void DepositToAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVmFromSender(sender);
            if (vm == null)
            {
                ToastService.Info("Nie udało się odczytać rachunku.");
                return;
            }

            if (sender is FrameworkElement fe)
                ShowOperationPanel(BankOperationKind.Deposit, vm, fe);
        }

        private void OpSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAccount == null || _currentOperation == BankOperationKind.None)
            {
                HideOperationPanel();
                return;
            }

            OpErrorText.Visibility = Visibility.Collapsed;
            OpErrorText.Text = "";

            var txt = (OpAmountBox.Text ?? "").Replace(" ", "");
            if (!decimal.TryParse(txt, NumberStyles.Number, CultureInfo.CurrentCulture,
                                  out var amount) || amount <= 0)
            {
                OpErrorText.Text = "Podaj poprawną dodatnią kwotę.";
                OpErrorText.Visibility = Visibility.Visible;
                return;
            }

            bool useEnvelope = OpFromEnvelopeRadio.IsChecked == true;

            if (useEnvelope && OpEnvelopeCombo.SelectedItem == null)
            {
                OpErrorText.Text = "Wybierz kopertę lub zmień źródło.";
                OpErrorText.Visibility = Visibility.Visible;
                return;
            }

            string cashType = "free";
            if (!useEnvelope && OpCashCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag)
                cashType = tag;

            try
            {
                switch (_currentOperation)
                {
                    case BankOperationKind.Withdraw:
                        if (useEnvelope)
                        {
                            ToastService.Info("Przelew z konta do koperty dodamy po spięciu modułu kopert.");
                        }
                        else
                        {
                            if (cashType == "free")
                            {
                                DatabaseService.TransferBankToCash(_uid, _currentAccount.Id, amount);
                                ToastService.Success($"Wypłacono {amount:N2} zł do wolnej gotówki.");
                            }
                            else
                            {
                                ToastService.Info("„Gotówka odłożona” będzie dostępna później.");
                            }
                        }
                        break;

                    case BankOperationKind.Deposit:
                        if (useEnvelope)
                        {
                            ToastService.Info("Przelew z koperty na konto dodamy po spięciu modułu kopert.");
                        }
                        else
                        {
                            if (cashType == "free")
                            {
                                DatabaseService.TransferCashToBank(_uid, _currentAccount.Id, amount);
                                ToastService.Success($"Wpłacono {amount:N2} zł na konto.");
                            }
                            else
                            {
                                ToastService.Info("Wpłata z „gotówki odłożonej” będzie dostępna później.");
                            }
                        }
                        break;
                }

                LoadAccounts();
                RefreshMoney();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się wykonać operacji: " + ex.Message);
            }
            finally
            {
                HideOperationPanel();
            }
        }

        private void OpCancel_Click(object sender, RoutedEventArgs e)
        {
            HideOperationPanel();
        }

        // ===== PANEL EDYCJI / DODAWANIA KONTA ======

        private void ShowEditPanel(BankAccountModel model, bool isNew)
        {
            HideOperationPanel();

            _editModel = model;

            var titleName = string.IsNullOrWhiteSpace(model.AccountName)
                ? "(bez nazwy)"
                : model.AccountName;

            EditTitle.Text = isNew
                ? "Dodaj konto bankowe"
                : $"Edytuj konto \"{titleName}\"";

            EditErrorText.Text = "";
            EditErrorText.Visibility = Visibility.Collapsed;

            EditAccountNameBox.Text = model.AccountName ?? "";
            EditIbanBox.Text = model.Iban ?? "";
            EditBalanceBox.Text = model.Balance.ToString("N2", CultureInfo.CurrentCulture);

            SetSelectedBankForEdit(model.BankName);

            EditPanel.Visibility = Visibility.Visible;
            OperationPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowEditPanel(BankAccountModel model) => ShowEditPanel(model, isNew: false);

        private void HideEditPanel()
        {
            EditPanel.Visibility = Visibility.Collapsed;
            _editModel = null;
        }

        private void EditBankCombo_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            var bankName = ExtractBankName(EditBankCombo.SelectedItem);

            if (string.Equals(bankName, "Inny bank", StringComparison.OrdinalIgnoreCase))
            {
                EditBankCustomBox.Visibility = Visibility.Visible;
            }
            else
            {
                EditBankCustomBox.Visibility = Visibility.Collapsed;
                EditBankCustomBox.Text = "";
            }
        }

        private void SetSelectedBankForEdit(string? bankName)
        {
            // nic nie mamy – ustaw "Inny bank" + pokaż textbox
            if (string.IsNullOrWhiteSpace(bankName))
            {
                foreach (var item in EditBankCombo.Items)
                {
                    if (string.Equals(ExtractBankName(item), "Inny bank", StringComparison.OrdinalIgnoreCase))
                    {
                        EditBankCombo.SelectedItem = item;
                        break;
                    }
                }

                EditBankCustomBox.Visibility = Visibility.Visible;
                EditBankCustomBox.Text = "";
                return;
            }

            // próbujemy znaleźć dokładnie taki bank na liście
            foreach (var item in EditBankCombo.Items)
            {
                if (string.Equals(ExtractBankName(item), bankName, StringComparison.OrdinalIgnoreCase))
                {
                    EditBankCombo.SelectedItem = item;
                    EditBankCustomBox.Visibility = Visibility.Collapsed;
                    EditBankCustomBox.Text = "";
                    return;
                }
            }

            // nie znaleziono -> traktujemy jako "Inny bank" i wpisujemy nazwę własną
            foreach (var item in EditBankCombo.Items)
            {
                if (string.Equals(ExtractBankName(item), "Inny bank", StringComparison.OrdinalIgnoreCase))
                {
                    EditBankCombo.SelectedItem = item;
                    break;
                }
            }

            EditBankCustomBox.Visibility = Visibility.Visible;
            EditBankCustomBox.Text = bankName;
        }

        private void EditSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editModel == null)
            {
                HideEditPanel();
                return;
            }

            EditErrorText.Visibility = Visibility.Collapsed;
            EditErrorText.Text = "";

            var textBalance = (EditBalanceBox.Text ?? "").Replace(" ", "");
            if (!decimal.TryParse(textBalance, NumberStyles.Number, CultureInfo.CurrentCulture,
                                  out var balance) || balance < 0)
            {
                EditErrorText.Text = "Podaj poprawne saldo (≥ 0).";
                EditErrorText.Visibility = Visibility.Visible;
                return;
            }

            var bankFromCombo = GetSelectedEditBankName();
            var bankCustom = (EditBankCustomBox.Text ?? "").Trim();

            string finalBankName;
            if (string.Equals(bankFromCombo, "Inny bank", StringComparison.OrdinalIgnoreCase))
                finalBankName = string.IsNullOrWhiteSpace(bankCustom) ? "Inny bank" : bankCustom;
            else
                finalBankName = bankFromCombo;

            _editModel.BankName = finalBankName;
            _editModel.AccountName = (EditAccountNameBox.Text ?? "").Trim();
            _editModel.Iban = (EditIbanBox.Text ?? "").Trim();
            _editModel.Currency = string.IsNullOrWhiteSpace(_editModel.Currency) ? "PLN" : _editModel.Currency;
            _editModel.Balance = balance;

            try
            {
                var entity = ToEntity(_editModel);
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
            finally
            {
                HideEditPanel();
            }
        }

        private void EditCancel_Click(object sender, RoutedEventArgs e)
        {
            HideEditPanel();
        }

        // ===== KLIKNIĘCIE „EDYTUJ” NA KAFELKU =====

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

            ShowEditPanel(model);
        }

        // ===== KAFEL DODAJ KONTO =====

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

            ShowEditPanel(model, isNew: true);
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

            var c = new ConfirmDialog("Usunąć ten rachunek? Operacja nieodwracalna.")
            {
                Owner = Window.GetWindow(this)
            };

            if (c.ShowDialog() == true && c.Result)
            {
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
            Balance = m.Balance
        };
    }

    // ===== VM KAFELKA KONTA =====
    // ===== VM KAFELKA KONTA =====
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

            // JEDEN parametr – nazwa banku z bazy
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

    // marker dla kafla "Dodaj konto"
    public sealed class AddAccountTile
    {
    }

}
