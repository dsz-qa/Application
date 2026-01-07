using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Finly.Pages; // BudgetRow

namespace Finly.Views.Dialogs
{
    public partial class EditBudgetDialog : Window
    {
        public BudgetDialogViewModel Budget { get; private set; }

        private bool _isEditMode;
        private bool _isCustomRange;

        public EditBudgetDialog()
        {
            InitializeComponent();

            Budget = new BudgetDialogViewModel();
            DataContext = Budget;

            Loaded += (_, __) =>
            {
                HeaderTitleText.Text = "Dodawanie budżetu";

                Budget.StartDate ??= DateTime.Today;

                _isCustomRange = false;
                CustomRangePanel.Visibility = Visibility.Collapsed;

                HideInlineError();

                RecalcEndDateIfNeeded();
                EnsurePlannedNotEmpty();
            };
        }

        public void LoadBudget(BudgetRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            _isEditMode = true;
            HeaderTitleText.Text = "Edycja budżetu";

            Budget.Name = row.Name;

            var t = (row.Type ?? "").Trim();
            Budget.Type = t switch
            {
                "Tygodniowy" => "Weekly",
                "Miesięczny" => "Monthly",
                "Roczny" => "Yearly",
                "Weekly" => "Weekly",
                "Monthly" => "Monthly",
                "Yearly" => "Yearly",
                _ => "Monthly"
            };

            Budget.StartDate = row.StartDate;
            Budget.PlannedAmount = row.PlannedAmount;

            _isCustomRange = false;
            CustomRangePanel.Visibility = Visibility.Collapsed;

            HideInlineError();

            RecalcEndDateIfNeeded();
            EnsurePlannedNotEmpty();
        }

        // ======= TitleBar (drag + close) =======

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ======= Inline validation =======

        private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Jeśli użytkownik zaczyna pisać, chowamy błąd
            if (!string.IsNullOrWhiteSpace(NameTextBox.Text))
                HideInlineError();
        }

        private void ShowInlineError(string message, Control? focus = null)
        {
            if (NameErrorText != null)
            {
                NameErrorText.Text = message;
                NameErrorText.Visibility = Visibility.Visible;
            }

            focus?.Focus();
        }

        private void HideInlineError()
        {
            if (NameErrorText != null)
            {
                NameErrorText.Text = string.Empty;
                NameErrorText.Visibility = Visibility.Collapsed;
            }
        }

        // ===================== OKRES: custom vs predef =====================

        private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HideInlineError();

            if (PeriodCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag == "Custom")
            {
                _isCustomRange = true;
                CustomRangePanel.Visibility = Visibility.Visible;

                if (Budget.EndDate == null && Budget.StartDate != null)
                    Budget.EndDate = Budget.StartDate.Value;

                return;
            }

            _isCustomRange = false;
            CustomRangePanel.Visibility = Visibility.Collapsed;

            RecalcEndDateIfNeeded();
        }

        private void StartDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            HideInlineError();

            if (_isCustomRange)
            {
                if (Budget.EndDate == null && Budget.StartDate != null)
                    Budget.EndDate = Budget.StartDate.Value;

                return;
            }

            RecalcEndDateIfNeeded();
        }

        private void RecalcEndDateIfNeeded()
        {
            if (_isCustomRange) return;
            if (Budget.StartDate == null) return;

            var start = Budget.StartDate.Value.Date;
            var t = (Budget.Type ?? "Monthly").Trim();

            Budget.EndDate = t switch
            {
                "Weekly" => start.AddDays(6),
                "Monthly" => new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1),
                "Yearly" => new DateTime(start.Year, 12, 31),
                "OneTime" => start,
                "Rollover" => new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1),
                _ => new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1)
            };
        }

        // ===================== KWOTA =====================

        private void PlannedAmount_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tb = (TextBox)sender;
            if (!tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
            }
        }

        private void PlannedAmount_GotFocus(object sender, RoutedEventArgs e)
        {
            HideInlineError();

            var tb = (TextBox)sender;
            var t = (tb.Text ?? string.Empty).Trim();

            if (t == "0" || t == "0,00" || t == "0.00")
                tb.Clear();

            tb.SelectAll();
        }

        private void PlannedAmount_LostFocus(object sender, RoutedEventArgs e)
        {
            EnsurePlannedNotEmpty();
        }

        private void EnsurePlannedNotEmpty()
        {
            if (PlannedAmountTextBox == null) return;

            if (string.IsNullOrWhiteSpace(PlannedAmountTextBox.Text))
            {
                PlannedAmountTextBox.Text = "0,00";
                Budget.PlannedAmount = 0m;
                return;
            }

            var text = PlannedAmountTextBox.Text.Trim();

            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var val) ||
                decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out val))
            {
                Budget.PlannedAmount = val;
                PlannedAmountTextBox.Text = val.ToString("0.00", CultureInfo.CurrentCulture);
            }
            else
            {
                Budget.PlannedAmount = 0m;
                PlannedAmountTextBox.Text = "0,00";
            }
        }

        // ===================== ZAPIS / ANULUJ =====================

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BudgetDialogViewModel vm)
                return;

            HideInlineError();

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                ShowInlineError("Podaj nazwę budżetu.", NameTextBox);
                return;
            }

            if (vm.StartDate == null)
            {
                // inline, bez okienek
                ShowInlineError("Wybierz datę startu.", StartDatePicker);
                return;
            }

            if (_isCustomRange)
            {
                if (vm.EndDate == null)
                {
                    ShowInlineError("Wybierz datę końca.", EndDatePicker);
                    return;
                }

                if (vm.EndDate.Value.Date < vm.StartDate.Value.Date)
                {
                    ShowInlineError("Data końca nie może być wcześniejsza niż data startu.", EndDatePicker);
                    return;
                }
            }
            else
            {
                RecalcEndDateIfNeeded();
            }

            if (vm.PlannedAmount < 0)
            {
                ShowInlineError("Kwota planowana nie może być ujemna.", PlannedAmountTextBox);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
