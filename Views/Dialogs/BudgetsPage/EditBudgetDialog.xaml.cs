using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Finly.Pages;
using Finly.Helpers.Converters;

namespace Finly.Views.Dialogs
{
    public partial class EditBudgetDialog : Window
    {
        private sealed class BudgetCopyCandidateItem
        {
            public BudgetRow Source { get; set; } = new BudgetRow();
            public string DisplayText { get; set; } = string.Empty;
        }

        public BudgetDialogViewModel Budget { get; private set; }

        private readonly List<BudgetCopyCandidateItem> _copyCandidates = new();
        private bool _isEditMode;
        private bool _isCustomRange;

        public enum BudgetDialogMode
        {
            Add,
            Edit
        }

        public EditBudgetDialog()
        {
            InitializeComponent();

            Budget = new BudgetDialogViewModel();
            DataContext = Budget;

            SetMode(BudgetDialogMode.Add);
            Loaded += EditBudgetDialog_Loaded;
        }

        public void SetCopyCandidates(IEnumerable<BudgetRow>? budgets)
        {
            _copyCandidates.Clear();

            var today = DateTime.Today;

            if (budgets != null)
            {
                _copyCandidates.AddRange(
                    budgets
                        .Where(b => b != null && b.StartDate.Date <= today)
                        .OrderByDescending(b => b.EndDate)
                        .ThenByDescending(b => b.StartDate)
                        .ThenBy(b => b.Name)
                        .Select(b => new BudgetCopyCandidateItem
                        {
                            Source = b,
                            DisplayText = $"{b.Name} | {b.TypeDisplay} | {b.Period} | {b.PlannedAmount:N2} zł"
                        }));
            }

            if (CopyBudgetCombo != null)
            {
                CopyBudgetCombo.ItemsSource = _copyCandidates;
                CopyBudgetCombo.DisplayMemberPath = nameof(BudgetCopyCandidateItem.DisplayText);
                CopyBudgetCombo.SelectedIndex = -1;
            }

            RefreshCopyPanelVisibility();
        }

        private void EditBudgetDialog_Loaded(object sender, RoutedEventArgs e)
        {
            Budget.StartDate ??= DateTime.Today;
            Budget.Type = NormalizeBudgetType(Budget.Type);

            EnsurePeriodComboSelectedFromType();
            RefreshCopyPanelVisibility();
            HideInlineError();

            if (Budget.Type == "Custom")
            {
                _isCustomRange = true;
                CustomRangePanel.Visibility = Visibility.Visible;

                if (Budget.EndDate == null && Budget.StartDate != null)
                    Budget.EndDate = Budget.StartDate.Value.Date;
            }
            else
            {
                _isCustomRange = false;
                CustomRangePanel.Visibility = Visibility.Collapsed;
                RecalcEndDateIfNeeded();
            }

            EnsurePlannedTextFromVm();
        }

        public void SetMode(BudgetDialogMode mode)
        {
            _isEditMode = mode == BudgetDialogMode.Edit;

            if (HeaderTitleText != null)
                HeaderTitleText.Text = _isEditMode ? "Edycja budżetu" : "Dodawanie budżetu";

            if (HeaderSubtitleText != null)
            {
                HeaderSubtitleText.Text = _isEditMode
                    ? "Zmień dane budżetu, a następnie zapisz zmiany."
                    : "Ustaw okres i kwotę albo skopiuj poprzedni budżet.";
            }

            RefreshCopyPanelVisibility();
        }

        public void LoadBudget(BudgetRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            SetMode(BudgetDialogMode.Edit);

            Budget.Name = row.Name;
            Budget.PlannedAmount = row.PlannedAmount;

            var normalizedType = InferBudgetType(row);

            Budget.Type = normalizedType;
            Budget.StartDate = row.StartDate.Date;

            if (normalizedType == "Custom")
            {
                Budget.EndDate = row.EndDate.Date;
                _isCustomRange = true;
                CustomRangePanel.Visibility = Visibility.Visible;
            }
            else
            {
                Budget.EndDate = null;
                _isCustomRange = false;
                CustomRangePanel.Visibility = Visibility.Collapsed;
                RecalcEndDateIfNeeded();
            }

            EnsurePeriodComboSelectedFromType();
            HideInlineError();
            EnsurePlannedTextFromVm();
        }

        private static string NormalizeBudgetType(string? type)
        {
            var value = (type ?? string.Empty).Trim();

            if (value.Equals("Weekly", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Tygodniowy", StringComparison.OrdinalIgnoreCase))
                return "Weekly";

            if (value.Equals("Monthly", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Miesięczny", StringComparison.OrdinalIgnoreCase))
                return "Monthly";

            if (value.Equals("Yearly", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Roczny", StringComparison.OrdinalIgnoreCase))
                return "Yearly";

            if (value.Equals("Custom", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Inny", StringComparison.OrdinalIgnoreCase))
                return "Custom";

            return "Monthly";
        }

        private static string InferBudgetType(BudgetRow row)
        {
            var rawType = NormalizeBudgetType(row.Type);

            if (rawType != "Custom")
                return rawType;

            var start = row.StartDate.Date;
            var end = row.EndDate.Date;
            var days = (end - start).Days;

            var looksWeekly = days == 6;
            var looksMonthly = start.Day == 1 &&
                               end == new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            var looksYearly = start.Month == 1 &&
                              start.Day == 1 &&
                              end == new DateTime(start.Year, 12, 31);

            if (looksWeekly) return "Weekly";
            if (looksMonthly) return "Monthly";
            if (looksYearly) return "Yearly";

            return "Custom";
        }

        private void RefreshCopyPanelVisibility()
        {
            if (CopyBudgetPanel == null)
                return;

            CopyBudgetPanel.Visibility = !_isEditMode && _copyCandidates.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void EnsurePeriodComboSelectedFromType()
        {
            if (PeriodCombo == null)
                return;

            PeriodCombo.SelectedValue = NormalizeBudgetType(Budget.Type);
        }

        private void CopyBudgetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isEditMode)
                return;

            if (CopyBudgetCombo.SelectedItem is not BudgetCopyCandidateItem item || item.Source == null)
                return;

            ApplyCopyCandidate(item.Source);
        }

        private void ApplyCopyCandidate(BudgetRow source)
        {
            var normalizedType = InferBudgetType(source);
            var nextStart = source.EndDate.Date.AddDays(1);

            Budget.Name = source.Name;
            Budget.PlannedAmount = source.PlannedAmount;
            Budget.Type = normalizedType;
            Budget.StartDate = nextStart;

            if (normalizedType == "Custom")
            {
                var spanDays = Math.Max(0, (source.EndDate.Date - source.StartDate.Date).Days);
                Budget.EndDate = nextStart.AddDays(spanDays);

                _isCustomRange = true;
                CustomRangePanel.Visibility = Visibility.Visible;
            }
            else
            {
                Budget.EndDate = null;

                _isCustomRange = false;
                CustomRangePanel.Visibility = Visibility.Collapsed;
                RecalcEndDateIfNeeded();
            }

            EnsurePeriodComboSelectedFromType();
            HideInlineError();
            EnsurePlannedTextFromVm();
        }

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

        private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HideInlineError();

            var selectedType = NormalizeBudgetType(PeriodCombo.SelectedValue as string ?? Budget.Type);
            Budget.Type = selectedType;

            if (selectedType == "Custom")
            {
                _isCustomRange = true;
                CustomRangePanel.Visibility = Visibility.Visible;

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

                if (Budget.StartDate != null &&
                    Budget.EndDate != null &&
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
            var type = NormalizeBudgetType(Budget.Type);

            Budget.Type = type;
            Budget.EndDate = type switch
            {
                "Weekly" => start.AddDays(6),
                "Monthly" => new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1),
                "Yearly" => new DateTime(start.Year, 12, 31),
                _ => new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1)
            };
        }

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
            var text = (tb.Text ?? string.Empty).Trim();

            text = text.Replace(" ", "")
                       .Replace("\u00A0", "")
                       .Replace("\u202F", "")
                       .Replace("'", "");

            if (text.EndsWith(",00", StringComparison.Ordinal) || text.EndsWith(".00", StringComparison.Ordinal))
                text = text[..^3];

            if (text == "0" || text == "0," || text == "0." || string.IsNullOrWhiteSpace(text))
                tb.Clear();
            else
                tb.Text = text;

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

            if (FlexibleDecimalConverter.TryParseFlexibleDecimal(raw, out var value))
            {
                value = Math.Round(value, 2, MidpointRounding.AwayFromZero);

                Budget.PlannedAmount = value;
                PlannedAmountTextBox.Text = value.ToString("0.00", CultureInfo.CurrentCulture);
                return;
            }

            Budget.PlannedAmount = 0m;
            PlannedAmountTextBox.Text = 0m.ToString("0.00", CultureInfo.CurrentCulture);
        }

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

                vm.Type = "Custom";
            }
            else
            {
                RecalcEndDateIfNeeded();
                vm.Type = NormalizeBudgetType(vm.Type);
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