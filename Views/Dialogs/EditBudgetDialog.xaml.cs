using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Finly.Pages; // BudgetRow
using Finly.Helpers.Converters;

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
                EnsurePlannedTextFromVm();
            };
        }

        public void LoadBudget(BudgetRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            _isEditMode = true;
            HeaderTitleText.Text = "Edycja budżetu";

            Budget.Name = row.Name;

            var start = row.StartDate.Date;
            var end = row.EndDate.Date;

            var days = (end - start).Days;

            bool looksWeekly = days == 6;
            bool looksMonthly = end == new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            bool looksYearly = end == new DateTime(start.Year, 12, 31);

            var rawType = (row.Type ?? "").Trim();

            bool isExplicitCustom =
                rawType.Equals("Custom", StringComparison.OrdinalIgnoreCase) ||
                rawType.Equals("Inny", StringComparison.OrdinalIgnoreCase);

            bool isImplicitCustom = !looksWeekly && !looksMonthly && !looksYearly;

            if (isExplicitCustom || isImplicitCustom)
            {
                Budget.Type = "Custom";
                Budget.StartDate = start;
                Budget.EndDate = end;

                _isCustomRange = true;
                CustomRangePanel.Visibility = Visibility.Visible;

                Budget.PlannedAmount = row.PlannedAmount;

                HideInlineError();
                EnsurePlannedTextFromVm();
                return;
            }

            Budget.Type = looksWeekly ? "Weekly"
                        : looksYearly ? "Yearly"
                        : "Monthly";

            Budget.StartDate = start;
            Budget.PlannedAmount = row.PlannedAmount;

            _isCustomRange = false;
            CustomRangePanel.Visibility = Visibility.Collapsed;

            HideInlineError();
            RecalcEndDateIfNeeded();
            EnsurePlannedTextFromVm();
        }

        // ======= TitleBar =======

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

        // ===================== OKRES =====================

        private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HideInlineError();

            if (PeriodCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag == "Custom")
            {
                _isCustomRange = true;
                CustomRangePanel.Visibility = Visibility.Visible;

                // klucz: ustaw typ budżetu na Custom (Inny)
                Budget.Type = "Custom";

                if (Budget.EndDate == null && Budget.StartDate != null)
                    Budget.EndDate = Budget.StartDate.Value.Date;

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
                if (Budget.StartDate != null && Budget.EndDate == null)
                    Budget.EndDate = Budget.StartDate.Value.Date;

                if (Budget.StartDate != null && Budget.EndDate != null &&
                    Budget.EndDate.Value.Date < Budget.StartDate.Value.Date)
                {
                    Budget.EndDate = Budget.StartDate.Value.Date;
                }

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

            // zabezpieczenie: gdyby ktoś ręcznie wbił "Custom", nie licz automatycznie
            if (t.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                return;

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

        private void EnsurePlannedTextFromVm()
        {
            if (PlannedAmountTextBox == null) return;

            var val = Budget?.PlannedAmount ?? 0m;
            PlannedAmountTextBox.Text = val.ToString("0.00", CultureInfo.CurrentCulture);
        }

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

            t = t.Replace(" ", "").Replace("\u00A0", "").Replace("\u202F", "").Replace("'", "");

            if (t.EndsWith(",00", StringComparison.Ordinal) || t.EndsWith(".00", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 3);

            if (t == "0" || t == "0," || t == "0." || string.IsNullOrWhiteSpace(t))
                tb.Clear();
            else
                tb.Text = t;

            tb.SelectAll();
        }

        private void PlannedAmount_LostFocus(object sender, RoutedEventArgs e)
        {
            EnsurePlannedNotEmptyAndNormalize();
        }

        private void EnsurePlannedNotEmptyAndNormalize()
        {
            if (PlannedAmountTextBox == null) return;

            var raw = (PlannedAmountTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                Budget.PlannedAmount = 0m;
                PlannedAmountTextBox.Text = 0m.ToString("0.00", CultureInfo.CurrentCulture);
                return;
            }

            if (FlexibleDecimalConverter.TryParseFlexibleDecimal(raw, out var val))
            {
                val = Math.Round(val, 2, MidpointRounding.AwayFromZero);

                Budget.PlannedAmount = val;
                PlannedAmountTextBox.Text = val.ToString("0.00", CultureInfo.CurrentCulture);
                return;
            }

            Budget.PlannedAmount = 0m;
            PlannedAmountTextBox.Text = 0m.ToString("0.00", CultureInfo.CurrentCulture);
        }

        // ===================== ZAPIS =====================

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BudgetDialogViewModel vm)
                return;

            HideInlineError();
            EnsurePlannedNotEmptyAndNormalize();

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                ShowInlineError("Podaj nazwę budżetu.", NameTextBox);
                return;
            }

            if (vm.StartDate == null)
            {
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

                // gwarancja: custom zapisuje się jako Custom
                vm.Type = "Custom";
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
