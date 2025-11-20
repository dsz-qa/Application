using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Data;

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

        // źródło / cel operacji Wpłać/Wypłać: free / saved / envelope
        private string _opSourceKind = "free";

        // ====== pomocniczy model koperty do ComboBoxa ======
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

        // ===== POMOCNICZE – wyciąganie nazwy banku z ComboBoxItem =====
        private static string ExtractBankName(object? item)
        {
            if (item is ComboBoxItem ci)
            {
                if (ci.Content is string s)
                    return s.Trim();

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

        // ===== KOPERTY – wczytanie do ComboBoxa =====
        private void LoadEnvelopesForOperation()
        {
            if (_uid <= 0) return;

            try
            {
                var dt = DatabaseService.GetEnvelopesTable(_uid);
                var list = new List<EnvelopeItem>();

                foreach (DataRow row in dt.Rows)
                {
                    var item = new EnvelopeItem
                    {
                        Id = Convert.ToInt32(row["Id"]),
                        Name = (row["Name"]?.ToString() ?? "(bez nazwy)").Trim(),
                        Allocated = row["Allocated"] == DBNull.Value
                            ? 0m
                            : Convert.ToDecimal(row["Allocated"])
                    };
                    list.Add(item);
                }

                OpEnvelopeCombo.ItemsSource = list;
                if (list.Count > 0)
                    OpEnvelopeCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się wczytać kopert: " + ex.Message);
                OpEnvelopeCombo.ItemsSource = null;
            }
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
                OpQuestionLabel.Text = "Dokąd chcesz wypłacić swoje pieniądze?";
            }
            else
            {
                OpTitle.Text = $"Wpłata na konto \"{vm.AccountName}\"";
                OpQuestionLabel.Text = "Skąd chcesz wpłacić pieniądze na konto?";
            }

            OpAmountBox.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
            OpErrorText.Text = "";
            OpErrorText.Visibility = Visibility.Collapsed;

            // domyślnie wolna gotówka
            _opSourceKind = "free";
            OpFreeCashButton.IsChecked = true;
            OpSavedCashButton.IsChecked = false;
            OpEnvelopeButton.IsChecked = false;
            OpEnvelopeRow.Visibility = Visibility.Collapsed;

            // wczytaj aktualne koperty
            LoadEnvelopesForOperation();

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

        private void OpSourceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton btn) return;

            string kind = btn.Tag as string ?? "free";
            _opSourceKind = kind;

            OpFreeCashButton.IsChecked = kind == "free";
            OpSavedCashButton.IsChecked = kind == "saved";
            OpEnvelopeButton.IsChecked = kind == "envelope";

            OpEnvelopeRow.Visibility = kind == "envelope"
                ? Visibility.Visible
                : Visibility.Collapsed;
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

            if (_opSourceKind == "envelope" && OpEnvelopeCombo.SelectedItem == null)
            {
                OpErrorText.Text = "Wybierz kopertę.";
                OpErrorText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                switch (_currentOperation)
                {
                    case BankOperationKind.Withdraw:
                        HandleWithdraw(amount);
                        break;

                    case BankOperationKind.Deposit:
                        HandleDeposit(amount);
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

        private void HandleWithdraw(decimal amount)
        {
            if (_currentAccount == null) return;

            switch (_opSourceKind)
            {
                case "free":
                    // konto -> wolna gotówka
                    DatabaseService.TransferBankToCash(_uid, _currentAccount.Id, amount);
                    ToastService.Success($"Wypłacono {amount:N2} zł do wolnej gotówki.");
                    break;

                case "saved":
                    // konto -> gotówka -> odłożona gotówka
                    DatabaseService.TransferBankToCash(_uid, _currentAccount.Id, amount);
                    DatabaseService.AddToSavedCash(_uid, amount);
                    ToastService.Success($"Wypłacono {amount:N2} zł do odłożonej gotówki.");
                    break;

                case "envelope":
                    // konto -> gotówka -> odłożona gotówka -> konkretna koperta
                    if (OpEnvelopeCombo.SelectedItem is not EnvelopeItem env)
                        throw new InvalidOperationException("Nie wybrano koperty.");

                    DatabaseService.TransferBankToCash(_uid, _currentAccount.Id, amount);
                    DatabaseService.AddToSavedCash(_uid, amount);
                    DatabaseService.AddToEnvelopeAllocated(_uid, env.Id, amount);

                    ToastService.Success(
                        $"Przelano {amount:N2} zł z konta do koperty \"{env.Name}\".");
                    break;
            }
        }

        private void HandleDeposit(decimal amount)
        {
            if (_currentAccount == null) return;

            switch (_opSourceKind)
            {
                case "free":
                    // wolna gotówka -> konto
                    DatabaseService.TransferCashToBank(_uid, _currentAccount.Id, amount);
                    ToastService.Success($"Wpłacono {amount:N2} zł na konto.");
                    break;

                case "saved":
                    // odłożona gotówka -> gotówka -> konto
                    DatabaseService.TransferCashToBank(_uid, _currentAccount.Id, amount);
                    DatabaseService.SubtractFromSavedCash(_uid, amount);
                    ToastService.Success($"Wpłacono {amount:N2} zł z odłożonej gotówki na konto.");
                    break;

                case "envelope":
                    // koperta -> odłożona gotówka -> gotówka -> konto
                    if (OpEnvelopeCombo.SelectedItem is not EnvelopeItem env)
                        throw new InvalidOperationException("Nie wybrano koperty.");

                    DatabaseService.TransferCashToBank(_uid, _currentAccount.Id, amount);
                    DatabaseService.SubtractFromEnvelopeAllocated(_uid, env.Id, amount);
                    DatabaseService.SubtractFromSavedCash(_uid, amount);

                    ToastService.Success(
                        $"Wpłacono {amount:N2} zł z koperty \"{env.Name}\" na konto.");
                    break;
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

            // RESET komunikatu IBAN przy otwarciu panelu
            IbanHintText.Text = "";
            IbanHintText.Visibility = Visibility.Collapsed;

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

            // 🔴 na start chowamy hint IBAN
            if (IbanHintText != null)
            {
                IbanHintText.Text = "Polski IBAN musi mieć dokładnie 28 znaków (PL + 26 cyfr).";
                IbanHintText.Visibility = Visibility.Collapsed;
            }

            // saldo
            var textBalance = (EditBalanceBox.Text ?? "").Replace(" ", "");
            if (!decimal.TryParse(textBalance, NumberStyles.Number, CultureInfo.CurrentCulture,
                                  out var balance) || balance < 0)
            {
                EditErrorText.Text = "Podaj poprawne saldo (≥ 0).";
                EditErrorText.Visibility = Visibility.Visible;
                return;
            }

            // IBAN
            var rawIban = (EditIbanBox.Text ?? "").Trim();
            string finalIban = "";
            if (!string.IsNullOrEmpty(rawIban))
            {
                if (!ValidatePolishIban(rawIban, out var normalized, out string? error))
                {
                    // 🔴 zamiast EditErrorText – pokazujemy czerwony hint pod polem IBAN
                    if (IbanHintText != null)
                    {
                        IbanHintText.Text = error ?? "Nieprawidłowy numer IBAN.";
                        IbanHintText.Visibility = Visibility.Visible;
                    }
                    return;
                }

                finalIban = FormatPolishIban(normalized);
            }



            // bank
            var bankFromCombo = GetSelectedEditBankName();
            var bankCustom = (EditBankCustomBox.Text ?? "").Trim();

            string finalBankName;
            if (string.Equals(bankFromCombo, "Inny bank", StringComparison.OrdinalIgnoreCase))
                finalBankName = string.IsNullOrWhiteSpace(bankCustom) ? "Inny bank" : bankCustom;
            else
                finalBankName = bankFromCombo;

            _editModel.BankName = finalBankName;
            _editModel.AccountName = (EditAccountNameBox.Text ?? "").Trim();
            _editModel.Iban = finalIban;
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


        private void EditIbanBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IbanHintText != null)
                IbanHintText.Visibility = Visibility.Collapsed;
        }


        private void EditCancel_Click(object sender, RoutedEventArgs e)
        {
            HideEditPanel();
        }

        // Kwota 0,00 – zachowanie (saldo w edycji konta)
        private void EditBalanceBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var txt = (tb.Text ?? "").Trim();

                if (string.IsNullOrEmpty(txt) ||
                    txt == "0" ||
                    txt == "0,00" ||
                    txt == "0.00" ||
                    txt == 0m.ToString("N2", CultureInfo.CurrentCulture))
                {
                    tb.Clear();
                }

                tb.SelectAll();
            }
        }

        private void EditBalanceBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var txt = (tb.Text ?? "").Trim();

                if (string.IsNullOrEmpty(txt))
                {
                    tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
                }
                else if (decimal.TryParse(txt, NumberStyles.Number,
                                          CultureInfo.CurrentCulture, out var value))
                {
                    tb.Text = value.ToString("N2", CultureInfo.CurrentCulture);
                }
                else
                {
                    tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
                }
            }
        }

        // Kwota 0,00 – zachowanie (panel operacji)
        private void OpAmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var txt = (tb.Text ?? "").Trim();

                if (string.IsNullOrEmpty(txt) ||
                    txt == "0" ||
                    txt == "0,00" ||
                    txt == "0.00" ||
                    txt == 0m.ToString("N2", CultureInfo.CurrentCulture))
                {
                    tb.Clear();
                }

                tb.SelectAll();
            }
        }

        private void OpAmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var txt = (tb.Text ?? "").Trim();

                if (string.IsNullOrEmpty(txt))
                {
                    tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
                }
                else if (decimal.TryParse(txt, NumberStyles.Number,
                                          CultureInfo.CurrentCulture, out var value))
                {
                    tb.Text = value.ToString("N2", CultureInfo.CurrentCulture);
                }
                else
                {
                    tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
                }
            }
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

        // ===== IBAN – prosta walidacja i format =====

        private static bool ValidatePolishIban(string input, out string normalized, out string? error)
        {
            normalized = "";
            error = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Numer IBAN jest pusty.";
                return false;
            }

            var s = input.ToUpperInvariant().Replace(" ", "");

            if (!s.StartsWith("PL"))
            {
                error = "Polski IBAN musi zaczynać się od 'PL'.";
                return false;
            }

            if (s.Length != 28)
            {
                error = "Polski IBAN musi mieć dokładnie 28 znaków (PL + 26 cyfr).";
                return false;
            }

            for (int i = 2; i < 28; i++)
            {
                if (!char.IsDigit(s[i]))
                {
                    error = "Po 'PL' w numerze IBAN mogą występować tylko cyfry.";
                    return false;
                }
            }

            normalized = s;
            return true;
        }

        private static string FormatPolishIban(string normalized)
        {
            // normalized: "PL" + 26 cyfr, bez spacji
            if (string.IsNullOrWhiteSpace(normalized))
                return "";

            var s = normalized.ToUpperInvariant().Replace(" ", "");
            if (s.Length != 28 || !s.StartsWith("PL"))
                return normalized;

            string country = s.Substring(0, 2); // PL
            string check = s.Substring(2, 2);
            string rest = s.Substring(4);      // 24 cyfry

            // grupujemy resztę po 4 cyfry
            var parts = new List<string> { country + check };
            for (int i = 0; i < rest.Length; i += 4)
            {
                int len = Math.Min(4, rest.Length - i);
                parts.Add(rest.Substring(i, len));
            }

            return string.Join(" ", parts);
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

    // marker dla kafla "Dodaj konto"
    public sealed class AddAccountTile
    {
    }
}

