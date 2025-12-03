using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Finly.Services;

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private readonly ObservableCollection<BudgetRow> _allBudgets = new();
        private readonly int _currentUserId = 1; // TODO: podmień na id zalogowanego użytkownika

        public BudgetsPage()
        {
            InitializeComponent();

            LoadBudgetsFromDatabase();
            SetupDefaultFilters();

            ApplyFilters();
            UpdateDetails();
        }

        // =================== ŁADOWANIE Z TABELI BUDGETS ===================

        private void LoadBudgetsFromDatabase()
        {
            _allBudgets.Clear();

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    b.Id,
                    b.Name,
                    b.Type,
                    b.StartDate,
                    b.EndDate,
                    b.PlannedAmount,
                    IFNULL((
                        SELECT SUM(e.Amount)
                        FROM Expenses e
                        WHERE e.UserId = b.UserId
                          AND e.Date >= b.StartDate
                          AND e.Date <= b.EndDate
                    ), 0) AS SpentAmount
                FROM Budgets b
                WHERE b.UserId = $uid
                ORDER BY b.StartDate;
            ";
            cmd.Parameters.AddWithValue("$uid", _currentUserId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new BudgetRow
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"].ToString() ?? string.Empty,
                    // 🔹 typ BIERZEMY Z TABELI Budgets.Type
                    Type = reader["Type"].ToString() ?? string.Empty,
                    TypeDisplay = reader["Type"].ToString() ?? string.Empty,
                    StartDate = Convert.ToDateTime(reader["StartDate"]),
                    EndDate = Convert.ToDateTime(reader["EndDate"]),
                    PlannedAmount = Convert.ToDecimal(reader["PlannedAmount"]),
                    SpentAmount = Convert.ToDecimal(reader["SpentAmount"])
                };

                row.Recalculate();
                _allBudgets.Add(row);
            }
        }

        private void SetupDefaultFilters()
        {
            if (_allBudgets.Any())
            {
                FromDatePicker.SelectedDate = _allBudgets.Min(b => b.StartDate);
                ToDatePicker.SelectedDate = _allBudgets.Max(b => b.EndDate);
            }
            else
            {
                var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                FromDatePicker.SelectedDate = start;
                ToDatePicker.SelectedDate = end;
            }
        }

        // =================== DODAWANIE / EDYCJA / USUWANIE ===================

        private void AddBudget_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Views.Dialogs.EditBudgetDialog
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();
            if (result != true || dialog.Budget == null)
                return;

            var vm = dialog.Budget;

            // zapis do bazy przez BudgetService
            var newId = BudgetService.InsertBudget(_currentUserId, vm);

            var row = new BudgetRow
            {
                Id = newId,
                Name = vm.Name,
                Type = vm.Type ?? "Budżet",
                TypeDisplay = vm.Type ?? "Budżet",
                StartDate = vm.StartDate ?? DateTime.Today,
                EndDate = vm.EndDate ?? vm.StartDate ?? DateTime.Today,
                PlannedAmount = vm.PlannedAmount,
                SpentAmount = 0m
            };

            row.Recalculate();
            _allBudgets.Add(row);

            // odśwież z bazy, żeby policzyć Wydano
            LoadBudgetsFromDatabase();
            ApplyFilters();
            SelectRow(row);
        }

        private void EditBudget_Click(object sender, RoutedEventArgs e)
        {
            var budget = GetSelectedBudgetFromGrid();
            if (budget == null)
                return;

            var dialog = new Views.Dialogs.EditBudgetDialog
            {
                Owner = Window.GetWindow(this)
            };

            dialog.LoadBudget(budget);
            var result = dialog.ShowDialog();
            if (result != true || dialog.Budget == null)
                return;

            var vm = dialog.Budget;

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE Budgets
                    SET Name = $name,
                        Type = $type,
                        StartDate = $start,
                        EndDate = $end,
                        PlannedAmount = $planned
                    WHERE Id = $id AND UserId = $uid;
                ";
                cmd.Parameters.AddWithValue("$id", budget.Id);
                cmd.Parameters.AddWithValue("$uid", _currentUserId);
                cmd.Parameters.AddWithValue("$name", vm.Name);
                cmd.Parameters.AddWithValue("$type", vm.Type ?? "Budżet");
                cmd.Parameters.AddWithValue("$start", vm.StartDate ?? DateTime.Today);
                cmd.Parameters.AddWithValue("$end", vm.EndDate ?? vm.StartDate ?? DateTime.Today);
                cmd.Parameters.AddWithValue("$planned", vm.PlannedAmount);
                cmd.ExecuteNonQuery();
            }

            LoadBudgetsFromDatabase();
            ApplyFilters();
        }

        private void DeleteBudget_Click(object sender, RoutedEventArgs e)
        {
            var budget = GetSelectedBudgetFromGrid();
            if (budget == null)
                return;

            var confirm = MessageBox.Show(
                $"Czy na pewno chcesz usunąć budżet „{budget.Name}”?",
                "Usuń budżet",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            using var conn = DatabaseService.GetConnection();
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Budgets WHERE Id = $id AND UserId = $uid;";
                cmd.Parameters.AddWithValue("$id", budget.Id);
                cmd.Parameters.AddWithValue("$uid", _currentUserId);
                cmd.ExecuteNonQuery();
            }

            _allBudgets.Remove(budget);
            ApplyFilters();
            UpdateDetails();
        }

        // =================== FILTRY I SZCZEGÓŁY ===================

        private void FilterButton_Click(object sender, RoutedEventArgs e) => ApplyFilters();

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            TypeFilterCombo.SelectedIndex = -1;
            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;
            SearchBox.Text = string.Empty;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            IEnumerable<BudgetRow> query = _allBudgets;

            var selectedTypeItem = TypeFilterCombo.SelectedItem as ComboBoxItem;
            var typeValue = selectedTypeItem?.Tag as string ?? selectedTypeItem?.Content as string;

            if (!string.IsNullOrWhiteSpace(typeValue) &&
                !string.Equals(typeValue, "Wszystkie", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(b => string.Equals(b.Type, typeValue, StringComparison.OrdinalIgnoreCase));
            }

            DateTime? from = FromDatePicker.SelectedDate;
            DateTime? to = ToDatePicker.SelectedDate;

            if (from.HasValue)
                query = query.Where(b => b.EndDate >= from.Value);
            if (to.HasValue)
                query = query.Where(b => b.StartDate <= to.Value);

            var search = (SearchBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(b =>
                    (b.Name ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (b.TypeDisplay ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var result = query
                .OrderBy(b => b.StartDate)
                .ThenBy(b => b.Name)
                .ToList();

            BudgetsGrid.ItemsSource = result;
            UpdateSummary(result);
            UpdateDetails();
        }

        private void UpdateSummary(IReadOnlyCollection<BudgetRow> current)
        {
            if (current == null || current.Count == 0)
            {
                StatusText.Text = "Brak budżetów w aktualnym widoku.";
                TotalPlannedText.Text = "0,00 zł";
                TotalSpentText.Text = "0,00 zł";
                TotalRemainingText.Text = "0,00 zł";
                return;
            }

            var planned = current.Sum(b => b.PlannedAmount);
            var spent = current.Sum(b => b.SpentAmount);
            var remaining = planned - spent;

            StatusText.Text = $"Budżetów w widoku: {current.Count}";
            TotalPlannedText.Text = $"{planned:N2} zł";
            TotalSpentText.Text = $"{spent:N2} zł";
            TotalRemainingText.Text = $"{remaining:N2} zł";
        }

        private void UpdateDetails()
        {
            var budget = GetSelectedBudgetFromGrid();
            if (budget == null)
            {
                DetailsNameText.Text = "Brak wybranego budżetu";
                DetailsPeriodText.Text = string.Empty;
                DetailsStatusText.Text = string.Empty;
                DetailsProgressBar.Value = 0;
                return;
            }

            DetailsNameText.Text = budget.Name;
            DetailsPeriodText.Text = $"{budget.StartDate:dd.MM.yyyy} – {budget.EndDate:dd.MM.yyyy}";
            DetailsStatusText.Text =
                $"{budget.StatusText} | Zaplanowano: {budget.PlannedAmount:N2} zł, " +
                $"wydano: {budget.SpentAmount:N2} zł, " +
                $"pozostało: {budget.RemainingAmount:N2} zł";
            DetailsProgressBar.Value = budget.ProgressPercent;
        }

        private BudgetRow? GetSelectedBudgetFromGrid() =>
            BudgetsGrid.SelectedItem as BudgetRow;

        private void BudgetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateDetails();

        private void SelectRow(BudgetRow row)
        {
            if (BudgetsGrid.ItemsSource is IEnumerable<BudgetRow> list)
            {
                var found = list.FirstOrDefault(b => b.Id == row.Id);
                if (found != null)
                    BudgetsGrid.SelectedItem = found;
            }
            UpdateDetails();
        }
    }

    public class BudgetRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string TypeDisplay { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public string Period => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}";

        public decimal PlannedAmount { get; set; }
        public decimal SpentAmount { get; set; }
        public decimal RemainingAmount => PlannedAmount - SpentAmount;

        public string StatusText { get; private set; } = string.Empty;
        public double ProgressPercent { get; private set; }

        public void Recalculate()
        {
            if (PlannedAmount <= 0)
                ProgressPercent = 0;
            else
            {
                var percentDecimal = Math.Clamp(
                    SpentAmount / (PlannedAmount == 0 ? 1 : PlannedAmount) * 100m,
                    0m,
                    999m);
                ProgressPercent = (double)percentDecimal;
            }

            var today = DateTime.Today;
            if (today < StartDate)
                StatusText = "Budżet jeszcze się nie rozpoczął";
            else if (today > EndDate)
                StatusText = "Budżet zakończony";
            else
                StatusText = "Budżet w trakcie";
        }
    }
}