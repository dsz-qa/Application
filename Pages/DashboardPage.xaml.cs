using Finly.Models;
using Finly.Services;
using Finly.ViewModels;
using Finly.Views;
using Finly.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl
    {
        private readonly int _userId;
        private List<ExpenseDisplayModel> _allExpenses = new();
        private List<ExpenseDisplayModel> _currentView = new();

        public DashboardPage() : this(GetCurrentUserIdSafe()) { }

        public DashboardPage(int userId)
        {
            InitializeComponent();
            _userId = userId;

            ExpenseGrid.MouseDoubleClick += ExpenseGrid_MouseDoubleClick;

            LoadExpenses();
            LoadCategories();
            ApplyFiltersAndRefresh();
        }

        private static int GetCurrentUserIdSafe()
        {
            try { return UserService.GetCurrentUserId(); }
            catch { return 0; }
        }

private void RefreshSnapshot()
    {
        var uid = Finly.Services.UserService.GetCurrentUserId();
        var s = DatabaseService.GetMoneySnapshot(uid);

        DbTotal.Text = s.Total.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        DbEnvelopes.Text = s.Envelopes.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        DbAvail.Text = s.AvailableToAllocate.ToString("N2", CultureInfo.CurrentCulture) + " zł";
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        RefreshSnapshot();
    }


    // ===== nawigacja =====
    private void AddExpenseButton_Click(object sender, RoutedEventArgs e)
            => (Window.GetWindow(this) as ShellWindow)?.NavigateTo("AddExpense");

        private void ShowChart_Click(object sender, RoutedEventArgs e)
            => (Window.GetWindow(this) as ShellWindow)?.NavigateTo("Charts");

        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ConfirmDialog(
                "Na pewno chcesz trwale usunąć konto wraz ze wszystkimi wydatkami i kategoriami?\nTej operacji nie można cofnąć.")
            { Owner = Window.GetWindow(this) };

            if (dlg.ShowDialog() == true && dlg.Result)
            {
                try
                {
                    if (!UserService.DeleteAccount(_userId))
                    {
                        ToastService.Error("Nie udało się usunąć konta.");
                        return;
                    }

                    ToastService.Success("Konto zostało usunięte.");
                    (Window.GetWindow(this) as ShellWindow)?.Close();

                    var auth = new AuthWindow();
                    if (auth.DataContext is AuthViewModel vm)
                        vm.ShowAccountDeletedInfo();

                    Application.Current.MainWindow = auth;
                    auth.Show();
                }
                catch (Exception ex)
                {
                    ToastService.Error("Błąd podczas usuwania konta: " + ex.Message);
                }
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var auth = new AuthWindow();
            if (auth.DataContext is AuthViewModel vm)
                vm.ShowLogoutInfo();

            (Window.GetWindow(this) as ShellWindow)?.Close();
            Application.Current.MainWindow = auth;
            auth.Show();
        }

        // ===== dane / filtry =====
        private void LoadExpenses()
        {
            _allExpenses = DatabaseService.GetExpensesWithCategoryNameByUser(_userId)
                .Select(e =>
                {
                    e.CategoryName = string.IsNullOrWhiteSpace(e.CategoryName)
                        ? "Brak kategorii"
                        : e.CategoryName.Trim();
                    e.Category = e.CategoryName; // alias dla kolumny DataGrid
                    return e;
                })
                .OrderByDescending(e => e.Date)
                .ToList();
        }

        private void LoadCategories()
        {
            var categories = _allExpenses
                .Select(e => e.CategoryName)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            CategoryFilterComboBox.ItemsSource = categories;
        }

        private void ApplyFiltersAndRefresh()
        {
            var selectedCategory = (CategoryFilterComboBox.Text ?? string.Empty).Trim();
            DateTime? from = FromDatePicker.SelectedDate;
            DateTime? to = ToDatePicker.SelectedDate;
            var query = (QueryTextBox?.Text ?? string.Empty).Trim();

            var filtered = _allExpenses
                .Where(exp =>
                       (string.IsNullOrWhiteSpace(selectedCategory) || exp.CategoryName == selectedCategory)
                    && (!from.HasValue || exp.Date >= from.Value)
                    && (!to.HasValue || exp.Date <= to.Value)
                    && (string.IsNullOrWhiteSpace(query) ||
                        (!string.IsNullOrWhiteSpace(exp.Description) &&
                         exp.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                .OrderByDescending(e => e.Date)
                .ToList();

            _currentView = filtered;
            ExpenseGrid.ItemsSource = _currentView;

            RefreshKpis(_currentView);
        }

        private void RefreshKpis(IReadOnlyCollection<ExpenseDisplayModel> set)
        {
            // Suma i liczba wpisów
            var total = set.Sum(e => e.Amount);
            if (TotalAmountText != null) TotalAmountText.Text = $"{total:0.00} zł";
            if (EntryCountText != null) EntryCountText.Text = set.Count.ToString();

            // Średnia dzienna
            if (set.Count > 0)
            {
                var min = set.Min(e => e.Date);
                var max = set.Max(e => e.Date);
                var days = Math.Max(1.0, (max - min).TotalDays + 1.0);
                var avg = total / days;
                if (DailyAverageText != null) DailyAverageText.Text = $"{avg:0.00} zł";
            }
            else
            {
                if (DailyAverageText != null) DailyAverageText.Text = "0,00 zł";
            }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e) => ApplyFiltersAndRefresh();

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            CategoryFilterComboBox.SelectedItem = null;
            CategoryFilterComboBox.Text = string.Empty;
            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;
            PresetRangeCombo.SelectedIndex = 0; // (brak)
            if (QueryTextBox != null) QueryTextBox.Text = string.Empty;

            ApplyFiltersAndRefresh();
        }

        private void PresetRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string? label = (PresetRangeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            RangePreset preset = label switch
            {
                "Dzisiaj" => RangePreset.Dzisiaj,
                "Ten tydzień" => RangePreset.TenTydzien,
                "Ten miesiąc" => RangePreset.TenMiesiac,
                "Ten rok" => RangePreset.TenRok,
                _ => RangePreset.Brak
            };

            DateRangeService.GetRange(preset, out var from, out var to);
            FromDatePicker.SelectedDate = from;
            ToDatePicker.SelectedDate = to;

            ApplyFiltersAndRefresh();
        }

        // ===== akcje na wierszu =====
        private void EditExpense_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var expense = DatabaseService.GetExpenseById(id);
                if (expense == null) return;

                var editView = new EditExpenseWindow(expense, _userId)
                {
                    Owner = Window.GetWindow(this)
                };
                editView.ShowDialog();

                LoadExpenses();
                LoadCategories();
                ApplyFiltersAndRefresh();
            }
        }

        private void DeleteExpense_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var dlg = new ConfirmDialog("Czy na pewno chcesz usunąć ten wydatek?")
                { Owner = Window.GetWindow(this) };

                if (dlg.ShowDialog() == true && dlg.Result)
                {
                    DatabaseService.DeleteExpense(id);
                    ToastService.Success("Usunięto wydatek.");

                    LoadExpenses();
                    LoadCategories();
                    ApplyFiltersAndRefresh();
                }
            }
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ExpenseGrid.SelectedItem is ExpenseDisplayModel item)
            {
                var dlg = new ConfirmDialog("Czy na pewno chcesz usunąć ten wydatek?")
                { Owner = Window.GetWindow(this) };

                if (dlg.ShowDialog() == true && dlg.Result)
                {
                    DatabaseService.DeleteExpense(item.Id);
                    ToastService.Success("Usunięto wydatek.");

                    LoadExpenses();
                    LoadCategories();
                    ApplyFiltersAndRefresh();
                }
            }
            else
            {
                ToastService.Info("Zaznacz wiersz, który chcesz usunąć.");
            }
        }

        private void ExpenseGrid_MouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            if (ExpenseGrid.SelectedItem is ExpenseDisplayModel selected)
            {
                var full = DatabaseService.GetExpenseById(selected.Id);
                if (full is null) return;

                var editView = new EditExpenseWindow(full, _userId)
                {
                    Owner = Window.GetWindow(this)
                };
                editView.ShowDialog();

                LoadExpenses();
                LoadCategories();
                ApplyFiltersAndRefresh();
            }
        }

        private void QuickAdd_Click(object sender, RoutedEventArgs e)
        {
            var text = QuickAddBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ToastService.Info("Wpisz kwotę i opcjonalny opis (np. 35,90 #zakupy).");
                return;
            }

            ToastService.Info("Szybkie dodawanie: wstaw tu parser i zapis do bazy 😊");
            QuickAddBox.Text = string.Empty;
        }
    }
}



