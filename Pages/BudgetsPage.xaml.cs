using Finly.Models;
using Finly.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using static Finly.Pages.BudgetRow;

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private readonly ObservableCollection<BudgetRow> _allBudgets = new();
        // TODO: tu później podepniesz id zalogowanego użytkownika
        private readonly int _currentUserId = 1;
        private readonly ObservableCollection<BudgetOperationRow> _currentBudgetOps = new();

        public BudgetsPage()
        {
            InitializeComponent();

            LoadBudgetsFromDatabase();
            SetupDefaultFilters();

            ApplyFilters();
            UpdateDetails();
            CheckAndNotifyOverBudgets();
            BudgetOperationsGrid.ItemsSource = _currentBudgetOps;
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
    ORDER BY b.StartDate;
            ";

            cmd.Parameters.AddWithValue("@uid", _currentUserId);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var row = new BudgetRow
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"].ToString() ?? string.Empty,
                    Type = reader["Type"].ToString() ?? string.Empty,
                    TypeDisplay = reader["Type"].ToString() ?? string.Empty,
                    StartDate = Convert.ToDateTime(reader["StartDate"]),
                    EndDate = Convert.ToDateTime(reader["EndDate"]),
                    PlannedAmount = reader.GetDecimal(reader.GetOrdinal("PlannedAmount")),
                    SpentAmount = reader.GetDecimal(reader.GetOrdinal("SpentAmount")),
                    IncomeAmount = reader.GetDecimal(reader.GetOrdinal("IncomeAmount"))
                };

                row.Recalculate();
                _allBudgets.Add(row);
            }
        }

        // =================== POWIADOMIENIA O PRZEKROCZENIU ===================

        private void CheckAndNotifyOverBudgets()
        {
            var newlyOver = new List<BudgetRow>();

            foreach (var b in _allBudgets)
            {
                var isOverNow = b.IsOverBudget;

                // było OK → zrobiło się przekroczone
                if (isOverNow && b.OverState == 0)
                {
                    newlyOver.Add(b);
                    MarkOverStateInDb(b.Id, 1);
                    b.OverState = 1;
                    b.OverNotifiedAt = DateTime.Today.ToString("yyyy-MM-dd");
                }

                // wróciło do OK → zdejmij flagę
                if (!isOverNow && b.OverState == 1)
                {
                    MarkOverStateInDb(b.Id, 0);
                    b.OverState = 0;
                }
            }

            if (newlyOver.Count > 0)
            {
                var msg = "Przekroczono budżety:\n\n" +
                          string.Join("\n", newlyOver.Select(x =>
                              $"• {x.Name} — o {x.OverAmount:N2} zł (okres: {x.StartDate:dd.MM.yyyy}–{x.EndDate:dd.MM.yyyy})"));

                MessageBox.Show(
                    msg,
                    "Przekroczenie budżetu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void MarkOverStateInDb(int budgetId, int state)
        {
            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE Budgets
SET OverState = $state,
    OverNotifiedAt = CASE WHEN $state = 1 THEN $dt ELSE OverNotifiedAt END
WHERE Id = $id AND UserId = $uid;";

            cmd.Parameters.AddWithValue("$state", state);
            cmd.Parameters.AddWithValue("$dt", DateTime.Today.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$id", budgetId);
            cmd.Parameters.AddWithValue("$uid", _currentUserId);
            cmd.ExecuteNonQuery();
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
                SpentAmount = 0m,
                IncomeAmount = 0m
            };

            row.Recalculate();

            // odśwież z bazy
            LoadBudgetsFromDatabase();

            //  TE DWIE LINIE SĄ KLUCZOWE
            SetupDefaultFilters();   // przelicz zakres dat na nowo
            ApplyFilters();          // zastosuj filtry do DataGrid

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
            SetupDefaultFilters();
            ApplyFilters();
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

        private void TransferBudget_Click(object sender, RoutedEventArgs e)
        {
            var current = GetSelectedBudgetFromGrid();
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
            if (result != true)
                return;

            var from = dialog.FromBudget!;
            var to = dialog.ToBudget!;
            var amount = dialog.Amount;

            if (amount <= 0)
                return;

            // Czy jest wystarczająco środków (patrzymy na RemainingAmount)?
            if (from.RemainingAmount < amount)
            {
                MessageBox.Show("W wybranym budżecie nie ma wystarczających środków.");
                return;
            }

            // ===== Zapis do bazy – przenosimy część planu między budżetami =====
            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var tran = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tran;

            // odejmij z budżetu źródłowego
            cmd.CommandText = @"
        UPDATE Budgets
        SET PlannedAmount = PlannedAmount - $amt
        WHERE Id = $id AND UserId = $uid;";
            cmd.Parameters.AddWithValue("$amt", amount);
            cmd.Parameters.AddWithValue("$id", from.Id);
            cmd.Parameters.AddWithValue("$uid", _currentUserId);
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();

            // dodaj do budżetu docelowego
            cmd.CommandText = @"
        UPDATE Budgets
        SET PlannedAmount = PlannedAmount + $amt
        WHERE Id = $id AND UserId = $uid;";
            cmd.Parameters.AddWithValue("$amt", amount);
            cmd.Parameters.AddWithValue("$id", to.Id);
            cmd.Parameters.AddWithValue("$uid", _currentUserId);
            cmd.ExecuteNonQuery();

            tran.Commit();

            // Odśwież dane w pamięci i UI
            LoadBudgetsFromDatabase();
            ApplyFilters();
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

            // nic nie zaznaczono
            if (budget == null)
            {
                BudgetNameText.Text = "(brak budżetu)";
                BudgetPeriodText.Text = string.Empty;
                BudgetPlannedText.Text = "0,00 zł";
                BudgetSpentText.Text = "0,00 zł";
                BudgetIncomeText.Text = "0,00 zł";
                BudgetRemainingText.Text = "0,00 zł";
                BudgetProgressBar.Value = 0;
                BudgetProgressPercentText.Text = "0%";
                BudgetStatusText.Text = string.Empty;
                BudgetTimeSummaryText.Text = string.Empty;

                // wyczyść wykres
                LoadBudgetHistoryChart(null);
                return;
            }

            // ====== PODSUMOWANIE KWOT ======
            BudgetNameText.Text = budget.Name;
            BudgetPeriodText.Text = $"{budget.StartDate:dd.MM.yyyy} – {budget.EndDate:dd.MM.yyyy}";

            BudgetPlannedText.Text = budget.PlannedAmount.ToString("N2") + " zł";
            BudgetSpentText.Text = budget.SpentAmount.ToString("N2") + " zł";
            BudgetIncomeText.Text = budget.IncomeAmount.ToString("N2") + " zł";
            BudgetRemainingText.Text = budget.RemainingAmount.ToString("N2") + " zł";

            var netSpent = budget.SpentAmount - budget.IncomeAmount;
            var percentUsed = budget.PlannedAmount == 0
                ? 0m
                : netSpent / budget.PlannedAmount * 100m;
            var usedClamped = Math.Max(0m, Math.Min(100m, percentUsed));

            BudgetProgressBar.Value = (double)usedClamped;
            BudgetProgressPercentText.Text = $"{usedClamped:0}%";

            BudgetStatusText.Text =
                $"Budżet w trakcie | plan: {budget.PlannedAmount:N2} zł, " +
                $"przychody: {budget.IncomeAmount:N2} zł, " +
                $"wydano: {budget.SpentAmount:N2} zł, " +
                $"pozostało: {budget.RemainingAmount:N2} zł.";

            // ====== POSTĘP W CZASIE ======
            var today = DateTime.Today;
            var totalDays = (budget.EndDate - budget.StartDate).TotalDays;
            var passed = (today - budget.StartDate).TotalDays;

            if (totalDays <= 0)
            {
                BudgetTimeSummaryText.Text = "Okres budżetu ma 1 dzień.";
            }
            else
            {
                if (passed < 0) passed = 0;
                if (passed > totalDays) passed = totalDays;

                var timePct = passed / totalDays * 100.0;
                BudgetTimeSummaryText.Text =
                    $"Dni: {passed:0} / {totalDays:0} ({timePct:0}% czasu budżetu minęło).";
            }

            // ====== HISTORIA – WYKRES ======
            LoadBudgetHistoryChart(budget);

            // ====== HISTORIA – LISTA OPERACJI ======
            LoadBudgetOperations(budget);
        }

        // =================== WYKRES HISTORII BUDŻETU ===================

        private void LoadBudgetHistoryChart(BudgetRow budget)
        {
            // jeżeli kontrolka jeszcze nie istnieje (np. w trakcie inicjalizacji) – nic nie rób
            if (BudgetHistoryChartControl == null)
                return;

            // NIC NIE ZAZNACZONO – czyścimy wykres
            if (budget == null)
            {
                BudgetHistoryChartControl.Series = Array.Empty<ISeries>();
                return;
            }

            // słownik: dzień -> (przychody, wydatki)
            var daily = new SortedDictionary<DateTime, (decimal income, decimal expense)>();

            using var con = DatabaseService.GetConnection();
            con.Open();

            // ----- PRZYCHODY W BUDŻECIE -----
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
            SELECT Date, IFNULL(SUM(Amount), 0) AS Total
            FROM Incomes
            WHERE UserId  = @uid
              AND BudgetId = @bid
            GROUP BY Date
            ORDER BY Date;";
                cmd.Parameters.AddWithValue("@uid", _currentUserId);
                cmd.Parameters.AddWithValue("@bid", budget.Id);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var dateStr = reader.GetString(0); // "yyyy-MM-dd"
                    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var total = (decimal)reader.GetDouble(1);

                    if (!daily.TryGetValue(date, out var tuple))
                        tuple = (0m, 0m);

                    tuple.income += total;
                    daily[date] = tuple;
                }
            }

            // ----- WYDATKI W BUDŻECIE -----
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
            SELECT Date, IFNULL(SUM(Amount), 0) AS Total
            FROM Expenses
            WHERE UserId  = @uid
              AND BudgetId = @bid
            GROUP BY Date
            ORDER BY Date;";
                cmd.Parameters.AddWithValue("@uid", _currentUserId);
                cmd.Parameters.AddWithValue("@bid", budget.Id);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var dateStr = reader.GetString(0);
                    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var total = (decimal)reader.GetDouble(1);

                    if (!daily.TryGetValue(date, out var tuple))
                        tuple = (0m, 0m);

                    tuple.expense += total;
                    daily[date] = tuple;
                }
            }


            if (daily.Count == 0)
            {
                BudgetHistoryChartControl.Series = Array.Empty<ISeries>();
                return;
            }

            // dane narastająco – widać rozwój budżetu
            var incomeValues = new List<double>();
            var expenseValues = new List<double>();

            decimal cumIncome = 0m;
            decimal cumExpense = 0m;

            foreach (var kvp in daily)
            {
                var (income, expense) = kvp.Value;

                cumIncome += income;
                cumExpense += expense;

                incomeValues.Add((double)cumIncome);
                expenseValues.Add((double)cumExpense);
            }

            BudgetHistoryChartControl.Series = new ISeries[]
            {
        new LineSeries<double>
        {
            Name         = "Przychody (narastająco)",
            Values       = incomeValues,
            GeometrySize = 5
        },
        new LineSeries<double>
        {
            Name         = "Wydatki (narastająco)",
            Values       = expenseValues,
            GeometrySize = 5
        }
            };
        }

        private void LoadBudgetOperations(BudgetRow? budget)
        {
            _currentBudgetOps.Clear();

            if (budget == null)
                return;

            using var con = DatabaseService.GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
        SELECT 
            e.Date               AS Date,
            e.Amount             AS Amount,
            'Wydatek'            AS Kind,
            IFNULL(c.Name, '')   AS Category,
            IFNULL(e.Description,'') AS Description
        FROM Expenses e
        LEFT JOIN Categories c ON e.CategoryId = c.Id
        WHERE e.UserId  = @uid
          AND e.BudgetId = @bid

        UNION ALL

        SELECT 
            i.Date               AS Date,
            i.Amount             AS Amount,
            'Przychód'           AS Kind,
            IFNULL(c.Name, '')   AS Category,
            IFNULL(i.Description,'') AS Description
        FROM Incomes i
        LEFT JOIN Categories c ON i.CategoryId = c.Id
        WHERE i.UserId  = @uid
          AND i.BudgetId = @bid

        ORDER BY Date;
    ";

            cmd.Parameters.AddWithValue("@uid", _currentUserId);
            cmd.Parameters.AddWithValue("@bid", budget.Id);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var dateStr = reader.GetString(0);           // "yyyy-MM-dd"
                var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var amount = (decimal)reader.GetDouble(1);
                var kind = reader.GetString(2);              // "Wydatek" albo "Przychód"
                var category = reader.GetString(3);
                var desc = reader.GetString(4);

                // dla czytelności możemy dać minus przy wydatkach
                if (kind == "Wydatek")
                    amount = -amount;

                _currentBudgetOps.Add(new BudgetOperationRow
                {
                    Date = date,
                    Kind = kind,
                    Category = category,
                    Amount = amount,
                    Description = desc
                });
            }


        }

        // =================== POMOCNICZE ===================

        private BudgetRow? GetSelectedBudgetFromGrid() =>
            BudgetsGrid.SelectedItem as BudgetRow;

        private void BudgetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetails();

            var b = GetSelectedBudgetFromGrid();
            if (b != null)
                LoadBudgetHistoryChart(b);
            if (BudgetHistoryChartControl == null)
            {
                MessageBox.Show("Wykres NULL – XAML nie zlinkowany");
                return;
            }
        }

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

    // =================== MODEL WIERSZA BUDŻETU ===================

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
        public decimal RemainingAmount { get; set; }
        public decimal IncomeAmount { get; set; }

        public string StatusText { get; private set; } = string.Empty;
        public double ProgressPercent { get; set; }

        public int OverState { get; set; }          // 0/1 z bazy
        public string? OverNotifiedAt { get; set; } // data tekstowo

        public bool IsOverBudget => RemainingAmount < 0;
        public decimal OverAmount => IsOverBudget ? Math.Abs(RemainingAmount) : 0m;

        public void Recalculate()
        {
            var total = PlannedAmount + IncomeAmount;
            RemainingAmount = total - SpentAmount;

            if (total <= 0)
                ProgressPercent = 0;
            else
                ProgressPercent = (double)(SpentAmount / total) * 100.0;

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

        public class BudgetOperationRow
        {
            public DateTime Date { get; set; }
            public string DateText => Date.ToString("dd.MM.yyyy");

            /// <summary>"Przychód" albo "Wydatek"</summary>
            public string Kind { get; set; } = string.Empty;

            public string Category { get; set; } = string.Empty;
            public decimal Amount { get; set; }

            // np. +200,00 zł / -350,00 zł
            public string AmountText => $"{Amount:N2} zł";

            public string Description { get; set; } = string.Empty;
        }

        public override string ToString()
        => Name;   //  TO DODAJ
    }
}