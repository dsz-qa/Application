using Finly.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    // Lokalna, bezpieczna baza pod binding (dialog nie zależy od Pages)
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    public sealed class InvestmentDialogViewModel : BindableBase
    {
        private string _name = "";
        public string Name { get => _name; set => Set(ref _name, value ?? ""); }

        // Tag string: Other/Stock/Bond/Etf/Fund/Crypto/Deposit/Gold
        private string _type = "Other";
        public string Type { get => _type; set => Set(ref _type, string.IsNullOrWhiteSpace(value) ? "Other" : value); }

        private string _description = "";
        public string Description { get => _description; set => Set(ref _description, value ?? ""); }
    }

    public partial class AddEditInvestmentDialog : Window
    {
        public InvestmentDialogViewModel Vm { get; private set; } = new();

        private bool _isEditMode;

        public AddEditInvestmentDialog()
        {
            InitializeComponent();
            DataContext = Vm;

            Loaded += (_, __) =>
            {
                try
                {
                    // Jeśli nie ustawiono trybu edycji – domyślnie Dodawanie
                    if (!_isEditMode)
                        HeaderTitleText.Text = "Dodawanie inwestycji";

                    HideInlineError();
                    EnsureTypeSelection();
                }
                catch
                {
                    // Dialog ma nie wywalać aplikacji nigdy
                }
            };
        }

        public void LoadForEdit(InvestmentModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            _isEditMode = true;

            // UWAGA: tu nie dotykamy kontrolek, jeśli jeszcze nie zainicjalizowane
            Vm.Name = model.Name ?? "";
            Vm.Type = EnumToTag(model.Type);
            Vm.Description = model.Description ?? "";

            // Jeżeli okno już załadowane, zsynchronizuj UI natychmiast
            if (IsLoaded)
            {
                try
                {
                    HeaderTitleText.Text = "Edycja inwestycji";
                    HideInlineError();
                    EnsureTypeSelection();
                }
                catch { }
            }
            else
            {
                // jeśli nie jest loaded, to ustaw nagłówek po załadowaniu
                Loaded += (_, __) =>
                {
                    try
                    {
                        HeaderTitleText.Text = "Edycja inwestycji";
                        HideInlineError();
                        EnsureTypeSelection();
                    }
                    catch { }
                };
            }
        }

        public InvestmentModel BuildModelForDb(int id, int userId)
        {
            // zero wyjątków przez trimming nulli
            var name = (Vm.Name ?? "").Trim();
            var desc = (Vm.Description ?? "").Trim();

            return new InvestmentModel
            {
                Id = id,
                UserId = userId,
                Name = name,
                Type = TagToEnum(Vm.Type),
                TargetAmount = 0m,
                CurrentAmount = 0m,
                TargetDate = null,
                Description = desc
            };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // DragMove potrafi rzucić wyjątek w nietypowych stanach
            try
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SafeClose(dialogResult: false);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SafeClose(dialogResult: false);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HideInlineError();

                var name = (Vm.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    ShowInlineError("Podaj nazwę inwestycji.", NameTextBox);
                    return;
                }

                // typ zawsze musi być poprawny
                if (string.IsNullOrWhiteSpace(Vm.Type))
                    Vm.Type = "Other";

                SafeClose(dialogResult: true);
            }
            catch (Exception ex)
            {
                // ostatnia linia obrony: nie crash, tylko komunikat inline
                ShowInlineError("Nie udało się zapisać: " + ex.Message);
            }
        }

        private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(NameTextBox.Text))
                    HideInlineError();
            }
            catch { }
        }

        // Awaryjne spięcie ComboBox->VM (WPF czasem nie zbinduje Tag jak trzeba)
        private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (TypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
                    Vm.Type = tag;
                else if (string.IsNullOrWhiteSpace(Vm.Type))
                    Vm.Type = "Other";
            }
            catch { }
        }

        private void EnsureTypeSelection()
        {
            try
            {
                var desired = string.IsNullOrWhiteSpace(Vm.Type) ? "Other" : Vm.Type;

                foreach (var obj in TypeCombo.Items)
                {
                    if (obj is ComboBoxItem it && (it.Tag as string) == desired)
                    {
                        TypeCombo.SelectedItem = it;
                        return;
                    }
                }

                // fallback: Other
                foreach (var obj in TypeCombo.Items)
                {
                    if (obj is ComboBoxItem it && (it.Tag as string) == "Other")
                    {
                        TypeCombo.SelectedItem = it;
                        Vm.Type = "Other";
                        return;
                    }
                }
            }
            catch { }
        }

        private void SafeClose(bool dialogResult)
        {
            // DialogResult może rzucać InvalidOperationException, jeśli window nie jest modalne
            try { DialogResult = dialogResult; } catch { }
            try { Close(); } catch { }
        }

        private void ShowInlineError(string message, Control? focus = null)
        {
            try
            {
                InlineErrorText.Text = message ?? "";
                InlineErrorText.Visibility = Visibility.Visible;
                focus?.Focus();
            }
            catch { }
        }

        private void HideInlineError()
        {
            try
            {
                InlineErrorText.Text = "";
                InlineErrorText.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private static InvestmentType TagToEnum(string? tag)
        {
            return (tag ?? "Other") switch
            {
                "Stock" => InvestmentType.Stock,
                "Bond" => InvestmentType.Bond,
                "Etf" => InvestmentType.Etf,
                "Fund" => InvestmentType.Fund,
                "Crypto" => InvestmentType.Crypto,
                "Deposit" => InvestmentType.Deposit,
                "Gold" => InvestmentType.Gold,
                _ => InvestmentType.Other
            };
        }

        private static string EnumToTag(InvestmentType t)
        {
            return t switch
            {
                InvestmentType.Stock => "Stock",
                InvestmentType.Bond => "Bond",
                InvestmentType.Etf => "Etf",
                InvestmentType.Fund => "Fund",
                InvestmentType.Crypto => "Crypto",
                InvestmentType.Deposit => "Deposit",
                InvestmentType.Gold => "Gold",
                _ => "Other"
            };
        }
    }
}
