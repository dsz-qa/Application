using Finly.Models;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private readonly ObservableCollection<BudgetRow> _allBudgets = new();
        private readonly int _currentUserId;

        public BudgetsPage(int userId)
        {
            InitializeComponent();
            _currentUserId = userId;

            // kafelek "Dodaj budżet" jak w Celach
            AddBudgetRepeater.ItemsSource = new object[] { new AddBudgetTile() };

            // typ domyślny: Wszystkie
            TypeFilterCombo.SelectedIndex = 0;

            LoadBudgetsFromDatabase();

            // Live selection z MiniTableControl (bo to nie DataGrid)
            Loaded += (_, __) =>
            {
                var dpd = DependencyPropertyDescriptor.FromProperty(
                    Finly.Views.Controls.MiniTableControl.SelectedItemProperty,
                    typeof(Finly.Views.Controls.MiniTableControl));

                dpd?.AddValueChanged(BudgetsTable, (_, __2) => UpdateTopKpisFromSelection());

                ApplyFilters(); // pierwszy render
            };
        }

        // bezpieczny konstruktor
        public BudgetsPage() : this(UserService.CurrentUserId) { }

        // =================== MAPOWANIA TYPÓW ===================

        private static string ToPlType(string? dbType)
        {
            return (dbType ?? "").Trim() switch
            {
                "Weekly" => "Tygodniowy",
                "Monthly" => "Miesięczny",
                "Yearly" => "Roczny",
                _ => dbType ?? ""
            };
        }

        private static string ToDbType(string? anyType)
        {
            var s = (anyType ?? "").Trim();

            // gdy dialog zwróci PL
            if (string.Equals(s, "Tygodniowy", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (string.Equals(s, "Miesięczny", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (string.Equals(s, "Roczny", StringComparison.OrdinalIgnoreCase)) return "Yearly";

            // gdy dialog zwróci DB
            if (string.Equals(s, "Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (string.Equals(s, "Monthly", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (string.Equals(s, "Yearly", StringComparison.OrdinalIgnoreCase)) return "Yearly";

            // fallback
            return "Monthly";
        }

        // =================== ŁADOWANIE Z BAZY ===================

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
    b.OverState,
    b.OverNotifiedAt,
    IFNULL((
        SELECT SUM(e.Amount)
        FROM Expenses e
        WHERE e.UserId  = b.UserId
          AND e.BudgetId = b.Id
          AND e.Date >= b.StartDate
          AND e.Date <= b.EndDate
    ), 0) AS SpentAmount,
    IFNULL((
        SELECT SUM(i.Amount)
        FROM Incomes i
        WHERE i.UserId  = b.UserId
          AND i.BudgetId = b.Id
          AND i.Date >= b.StartDate
          AND i.Date <= b.EndDate
    ), 0) AS IncomeAmount
FROM Budgets b
WHERE b.UserId = @uid
ORDER BY b.StartDate;";

            cmd.Parameters.AddWithValue("@uid", _currentUserId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var rawType = reader["Type"].ToString() ?? "Monthly";

                var row = new BudgetRow
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"].ToString() ?? string.Empty,

                    Type = rawType,
                    TypeDisplay = ToPlType(rawType),

                    StartDate = Convert.ToDateTime(reader["StartDate"]),
                    EndDate = Convert.ToDateTime(reader["EndDate"]),

                    PlannedAmount = reader.GetDecimal(reader.GetOrdinal("PlannedAmount")),
                    SpentAmount = reader.GetDecimal(reader.GetOrdinal("SpentAmount")),
                    IncomeAmount = reader.GetDecimal(reader.GetOrdinal("IncomeAmount")),

                    OverState = reader["OverState"] == DBNull.Value ? 0 : Convert.ToInt32(reader["OverState"]),
                    OverNotifiedAt = reader["OverNotifiedAt"] == DBNull.Value ? null : reader["OverNotifiedAt"].ToString(),
                };

                row.Recalculate();
                _allBudgets.Add(row);
            }
        }

        // =================== LIVE FILTERY ===================

        private void LiveFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();
        private void LiveFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            IEnumerable<BudgetRow> query = _allBudgets;

            // typ (Tag = Weekly/Monthly/Yearly)
            var selectedTypeItem = TypeFilterCombo.SelectedItem as ComboBoxItem;
            var typeValue = selectedTypeItem?.Tag as string ?? selectedTypeItem?.Content as string;

            if (!string.IsNullOrWhiteSpace(typeValue) &&
                !string.Equals(typeValue, "Wszystkie", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(b => string.Equals(b.Type, typeValue, StringComparison.OrdinalIgnoreCase));
            }

            // wyszukiwanie live
            var search = (SearchBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(b =>
                    (b.Name ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (b.TypeDisplay ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var result = query
                .OrderByDescending(b => b.StartDate)
                .ThenBy(b => b.Name)
                .ToList();

            BudgetsTable.ItemsSource = result;

            UpdateSummary(result);

            // jeśli nic nie zaznaczone, a mamy listę – zaznacz pierwszy
            if (BudgetsTable.SelectedItem == null && result.Count > 0)
                BudgetsTable.SelectedItem = result[0];

            UpdateTopKpisFromSelection();
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
            var remaining = current.Sum(b => b.RemainingAmount);

            StatusText.Text = $"Budżetów w widoku: {current.Count}";
            TotalPlannedText.Text = $"{planned:N2} zł";
            TotalSpentText.Text = $"{spent:N2} zł";
            TotalRemainingText.Text = $"{remaining:N2} zł";
        }

        // =================== KPI (góra) ===================

        private void UpdateTopKpisFromSelection()
        {
            var budget = BudgetsTable.SelectedItem as BudgetRow;

            if (budget == null)
            {
                SetTopKpis(null);
                return;
            }

            SetTopKpis(budget);
        }

        private void SetTopKpis(BudgetRow? b)
        {
            if (b == null)
            {
                KpiBudgetName_Planned.Text = "(brak budżetu)";
                KpiBudgetName_Spent.Text = "(brak budżetu)";
                KpiBudgetName_Remaining.Text = "(brak budżetu)";

                KpiBudgetType_Planned.Text = "";
                KpiBudgetType_Spent.Text = "";
                KpiBudgetType_Remaining.Text = "";

                KpiPlannedText.Text = "0,00 zł";
                KpiSpentText.Text = "0,00 zł";
                KpiRemainingText.Text = "0,00 zł";
                return;
            }

            var name = b.Name ?? "";
            var type = b.TypeDisplay ?? "";

            KpiBudgetName_Planned.Text = name;
            KpiBudgetName_Spent.Text = name;
            KpiBudgetName_Remaining.Text = name;

            KpiBudgetType_Planned.Text = type;
            KpiBudgetType_Spent.Text = type;
            KpiBudgetType_Remaining.Text = type;

            KpiPlannedText.Text = $"{b.PlannedAmount:N2} zł";
            KpiSpentText.Text = $"{b.SpentAmount:N2} zł";
            KpiRemainingText.Text = $"{b.RemainingAmount:N2} zł";
        }

        // =================== KAFEL „DODAJ BUDŻET” ===================

        private void AddBudgetCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AddBudget_Click(sender, new RoutedEventArgs());
        }

        // =================== DODAWANIE / EDYCJA / USUWANIE / PRZENOSZENIE ===================

        private BudgetRow? GetSelectedBudget() => BudgetsTable.SelectedItem as BudgetRow;

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

            // db typ stabilny
            vm.Type = ToDbType(vm.Type);

            var newId = BudgetService.InsertBudget(_currentUserId, vm);

            // odśwież z bazy (żeby policzyć wydano/przychody)
            LoadBudgetsFromDatabase();
            ApplyFilters();

            // ustaw zaznaczenie na nowy
            SelectById(newId);
        }

        private void EditBudget_Click(object sender, RoutedEventArgs e)
        {
            var budget = GetSelectedBudget();
            if (budget == null) return;

            var dialog = new Views.Dialogs.EditBudgetDialog
            {
                Owner = Window.GetWindow(this)
            };

            dialog.LoadBudget(budget);

            var result = dialog.ShowDialog();
            if (result != true || dialog.Budget == null)
                return;

            var vm = dialog.Budget;
            var dbType = ToDbType(vm.Type);

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE Budgets
SET Name = $name,
    Type = $type,
    StartDate = $start,
    EndDate = $end,
    PlannedAmount = $planned
WHERE Id = $id AND UserId = $uid;";

            cmd.Parameters.AddWithValue("$id", budget.Id);
            cmd.Parameters.AddWithValue("$uid", _currentUserId);
            cmd.Parameters.AddWithValue("$name", vm.Name);
            cmd.Parameters.AddWithValue("$type", dbType);
            cmd.Parameters.AddWithValue("$start", vm.StartDate ?? DateTime.Today);
            cmd.Parameters.AddWithValue("$end", vm.EndDate ?? vm.StartDate ?? DateTime.Today);
            cmd.Parameters.AddWithValue("$planned", vm.PlannedAmount);
            cmd.ExecuteNonQuery();

            LoadBudgetsFromDatabase();
            ApplyFilters();
            SelectById(budget.Id);
        }

        private void DeleteBudget_Click(object sender, RoutedEventArgs e)
        {
            var budget = GetSelectedBudget();
            if (budget == null) return;

            var confirm = MessageBox.Show(
                $"Czy na pewno chcesz usunąć budżet „{budget.Name}”?",
                "Usuń budżet",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Budgets WHERE Id = $id AND UserId = $uid;";
            cmd.Parameters.AddWithValue("$id", budget.Id);
            cmd.Parameters.AddWithValue("$uid", _currentUserId);
            cmd.ExecuteNonQuery();

            LoadBudgetsFromDatabase();
            ApplyFilters();
        }

        private void TransferBudget_Click(object sender, RoutedEventArgs e)
        {
            var current = GetSelectedBudget();
            if (current == null)
            {
                MessageBox.Show("Najpierw wybierz budżet, z którego chcesz przenieść środki.");
                return;
            }

            var dialog = new Views.Dialogs.TransferBudgetDialog(_allBudgets, current)
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();
            if (result != true) return;

            var from = dialog.FromBudget!;
            var to = dialog.ToBudget!;
            var amount = dialog.Amount;

            if (amount <= 0) return;

            if (from.RemainingAmount < amount)
            {
                MessageBox.Show("W wybranym budżecie nie ma wystarczających środków.");
                return;
            }

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var tran = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = @"
UPDATE Budgets
SET PlannedAmount = PlannedAmount - $amt
WHERE Id = $id AND UserId = $uid;";
                cmd.Parameters.AddWithValue("$amt", amount);
                cmd.Parameters.AddWithValue("$id", from.Id);
                cmd.Parameters.AddWithValue("$uid", _currentUserId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = @"
UPDATE Budgets
SET PlannedAmount = PlannedAmount + $amt
WHERE Id = $id AND UserId = $uid;";
                cmd.Parameters.AddWithValue("$amt", amount);
                cmd.Parameters.AddWithValue("$id", to.Id);
                cmd.Parameters.AddWithValue("$uid", _currentUserId);
                cmd.ExecuteNonQuery();
            }

            tran.Commit();

            LoadBudgetsFromDatabase();
            ApplyFilters();
            SelectById(to.Id);
        }

        private void SelectById(int id)
        {
            if (BudgetsTable.ItemsSource is IEnumerable<BudgetRow> list)
            {
                var found = list.FirstOrDefault(x => x.Id == id);
                if (found != null)
                    BudgetsTable.SelectedItem = found;
            }

            UpdateTopKpisFromSelection();
        }
    }

    // =================== VIEWMODELS / MODELE UI ===================

    public sealed class AddBudgetTile { }

    public class BudgetRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        // DB: Weekly/Monthly/Yearly
        public string Type { get; set; } = "Monthly";

        // UI: Tygodniowy/Miesięczny/Roczny
        public string TypeDisplay { get; set; } = "Miesięczny";

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public string Period => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}";

        public decimal PlannedAmount { get; set; }
        public decimal SpentAmount { get; set; }
        public decimal IncomeAmount { get; set; }

        public decimal RemainingAmount { get; set; }

        public int OverState { get; set; }          // 0/1 z bazy
        public string? OverNotifiedAt { get; set; } // data tekstowo

        public bool IsOverBudget => RemainingAmount < 0;
        public decimal OverAmount => IsOverBudget ? Math.Abs(RemainingAmount) : 0m;

        public string StatusText { get; private set; } = string.Empty;
        public double ProgressPercent { get; private set; }

        public void Recalculate()
        {
            // w Twojej logice: plan + przychody - wydatki
            var total = PlannedAmount + IncomeAmount;
            RemainingAmount = total - SpentAmount;

            ProgressPercent = total <= 0 ? 0 : (double)(SpentAmount / total) * 100.0;

            var today = DateTime.Today;

            if (today < StartDate)
                StatusText = "Budżet jeszcze się nie rozpoczął";
            else if (today > EndDate)
                StatusText = "Budżet zakończony";
            else
                StatusText = "Budżet w trakcie";

            if (IsOverBudget)
                StatusText = $"Przekroczony o {OverAmount:N2} zł";
        }

        public override string ToString() => Name;
    }
}
