using Finly.Models;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private readonly ObservableCollection<BudgetRow> _allBudgets = new();
        private readonly int _currentUserId;
        private bool _isInitialized;

        public BudgetsPage(int userId)
        {
            InitializeComponent();
            _currentUserId = userId;

            Loaded += BudgetsPage_Loaded;
        }

        public BudgetsPage() : this(UserService.CurrentUserId) { }

        private void BudgetsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            InitTypeFilterCombo();

            LoadBudgetsFromDatabase();
            ApplyFilters();
            UpdateTopKpis();

            if (BudgetsList != null && BudgetsList.SelectedItem == null && BudgetsList.Items.Count > 0)
                BudgetsList.SelectedIndex = 0;

            var selected = GetSelectedBudgetFromList();
            UpdateDetailsPanel(selected);
            LoadBudgetTransactions(selected);
        }

        // =================== TYPY ===================

        private void InitTypeFilterCombo()
        {
            if (TypeFilterCombo == null) return;

            TypeFilterCombo.Items.Clear();

            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Wszystkie", Tag = "" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Tygodniowy", Tag = "Weekly" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Miesięczny", Tag = "Monthly" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Roczny", Tag = "Yearly" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Inny (własny zakres)", Tag = "Custom" });

            TypeFilterCombo.SelectedIndex = 0;
        }

        private static string NormalizeDbType(string? dbType)
        {
            var t = (dbType ?? "Monthly").Trim();
            if (t.Equals("Inny", StringComparison.OrdinalIgnoreCase)) return "Custom";
            if (t.Equals("Custom", StringComparison.OrdinalIgnoreCase)) return "Custom";
            if (t.Equals("Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (t.Equals("Monthly", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (t.Equals("Yearly", StringComparison.OrdinalIgnoreCase)) return "Yearly";
            return "Monthly";
        }

        private static string ToPlType(string? dbType)
        {
            return NormalizeDbType(dbType) switch
            {
                "Weekly" => "Tygodniowy",
                "Monthly" => "Miesięczny",
                "Yearly" => "Roczny",
                "Custom" => "Inny",
                _ => "Miesięczny"
            };
        }

        private static string ToDbType(string? anyType)
        {
            var s = (anyType ?? "").Trim();

            if (s.Equals("Tygodniowy", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (s.Equals("Miesięczny", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (s.Equals("Roczny", StringComparison.OrdinalIgnoreCase)) return "Yearly";
            if (s.Equals("Inny", StringComparison.OrdinalIgnoreCase)) return "Custom";

            if (s.Equals("Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (s.Equals("Monthly", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (s.Equals("Yearly", StringComparison.OrdinalIgnoreCase)) return "Yearly";
            if (s.Equals("Custom", StringComparison.OrdinalIgnoreCase)) return "Custom";
            if (s.Equals("Inny", StringComparison.OrdinalIgnoreCase)) return "Custom";

            return "Monthly";
        }

        // =================== DB ===================

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
        WHERE e.UserId   = b.UserId
          AND e.BudgetId = b.Id
          AND date(e.Date) BETWEEN date(b.StartDate) AND date(b.EndDate)
    ), 0) AS SpentAmount,
    IFNULL((
        SELECT SUM(i.Amount)
        FROM Incomes i
        WHERE i.UserId   = b.UserId
          AND i.BudgetId = b.Id
          AND date(i.Date) BETWEEN date(b.StartDate) AND date(b.EndDate)
    ), 0) AS IncomeAmount
FROM Budgets b
WHERE b.UserId = @uid
  AND IFNULL(b.IsDeleted,0) = 0
ORDER BY date(b.StartDate);";

            cmd.Parameters.AddWithValue("@uid", _currentUserId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var rawType = reader["Type"]?.ToString();
                var normalizedType = NormalizeDbType(rawType);

                var row = new BudgetRow
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"]?.ToString() ?? string.Empty,

                    Type = normalizedType,
                    TypeDisplay = ToPlType(normalizedType),

                    StartDate = Convert.ToDateTime(reader["StartDate"], CultureInfo.InvariantCulture),
                    EndDate = Convert.ToDateTime(reader["EndDate"], CultureInfo.InvariantCulture),

                    PlannedAmount = ReadDecimal(reader, "PlannedAmount"),
                    SpentAmount = ReadDecimal(reader, "SpentAmount"),
                    IncomeAmount = ReadDecimal(reader, "IncomeAmount"),

                    OverState = reader["OverState"] == DBNull.Value ? 0 : Convert.ToInt32(reader["OverState"], CultureInfo.InvariantCulture),
                    OverNotifiedAt = reader["OverNotifiedAt"] == DBNull.Value ? null : reader["OverNotifiedAt"].ToString(),

                    IsDeleteConfirmVisible = false
                };

                row.Recalculate();
                _allBudgets.Add(row);
            }
        }

        private static decimal ReadDecimal(System.Data.IDataRecord reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return 0m;

            var obj = reader.GetValue(ordinal);
            return obj switch
            {
                decimal d => d,
                double dbl => Convert.ToDecimal(dbl, CultureInfo.InvariantCulture),
                float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
                long l => l,
                int i => i,
                string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) => v,
                _ => Convert.ToDecimal(obj, CultureInfo.InvariantCulture)
            };
        }

        // =================== LIVE FILTERY ===================

        private void TypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilters();
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (BudgetsList == null || TypeFilterCombo == null || SearchBox == null)
                return;

            var previouslySelectedId = (BudgetsList.SelectedItem as BudgetRow)?.Id;

            var typeValue = TypeFilterCombo.SelectedValue as string ?? "";

            IEnumerable<BudgetRow> query = _allBudgets;

            if (!string.IsNullOrWhiteSpace(typeValue))
            {
                query = query.Where(b =>
                    string.Equals(b.Type, typeValue, StringComparison.OrdinalIgnoreCase)
                    || (typeValue.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(b.Type, "Inny", StringComparison.OrdinalIgnoreCase)));
            }

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

            BudgetsList.ItemsSource = result;

            if (result.Count == 0)
            {
                BudgetsList.SelectedItem = null;
                UpdateDetailsPanel(null);
                LoadBudgetTransactions(null);
                return;
            }

            if (previouslySelectedId.HasValue)
            {
                var stillThere = result.FirstOrDefault(x => x.Id == previouslySelectedId.Value);
                if (stillThere != null)
                {
                    BudgetsList.SelectedItem = stillThere;
                    return;
                }
            }

            BudgetsList.SelectedIndex = 0;
        }

        private BudgetRow? GetSelectedBudgetFromList()
            => BudgetsList?.SelectedItem as BudgetRow;

        private void BudgetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HideAllDeleteConfirms();

            var selected = GetSelectedBudgetFromList();
            UpdateDetailsPanel(selected);
            LoadBudgetTransactions(selected);
        }

        // =================== PRAWA LISTA: TRANSAKCJE ===================

        private void LoadBudgetTransactions(BudgetRow? budget)
        {
            if (BudgetTransactionsList == null || BudgetTransactionsHintText == null)
                return;

            if (budget == null)
            {
                BudgetTransactionsList.ItemsSource = null;
                BudgetTransactionsHintText.Text = "Wybierz budżet z listy po lewej, aby zobaczyć jego transakcje.";
                return;
            }

            var rows = BudgetService.GetBudgetTransactions(
                userId: _currentUserId,
                budgetId: budget.Id,
                startDate: budget.StartDate.Date,
                endDate: budget.EndDate.Date);

            BudgetTransactionsList.ItemsSource = rows;

            BudgetTransactionsHintText.Text = rows.Count == 0
                ? "Brak transakcji przypisanych do tego budżetu w jego okresie."
                : $"Znaleziono: {rows.Count} transakcji w okresie budżetu.";
        }

        // =================== KPI OGÓLNE (góra) ===================

        private void UpdateTopKpis()
        {
            var today = DateTime.Today;

            var active = _allBudgets.Where(b => today >= b.StartDate.Date && today <= b.EndDate.Date).ToList();
            var activeCount = active.Count;

            var planned = active.Sum(b => b.PlannedAmount);
            var over = active.Where(b => b.IsOverBudget).ToList();
            var overCount = over.Count;
            var overAmount = over.Sum(b => b.OverAmount);

            if (KpiActiveBudgetsText != null) KpiActiveBudgetsText.Text = activeCount.ToString(CultureInfo.InvariantCulture);
            if (KpiPlannedActiveText != null) KpiPlannedActiveText.Text = $"{planned:N2} zł";
            if (KpiOverActiveText != null) KpiOverActiveText.Text = $"{overCount} | {overAmount:N2} zł";
        }

        // =================== PANEL (kafelki u góry) ===================

        private void UpdateDetailsPanel(BudgetRow? b)
        {
            if (BudgetNameText == null) return;

            if (b == null)
            {
                BudgetNameText.Text = "(wybierz budżet)";
                if (BudgetTypeText != null) BudgetTypeText.Text = "—";
                if (BudgetPeriodText != null) BudgetPeriodText.Text = "—";
                if (BudgetPlannedText != null) BudgetPlannedText.Text = "0,00 zł";
                if (BudgetSpentText != null) BudgetSpentText.Text = "0,00 zł";
                if (BudgetOverText != null) BudgetOverText.Text = "0,00 zł";
                if (BudgetMetaText != null) BudgetMetaText.Text = string.Empty;

                LoadBudgetHistoryChart(null);
                if (BudgetInsightsList != null) BudgetInsightsList.ItemsSource = new[] { "Brak danych." };
                return;
            }

            BudgetNameText.Text = b.Name;
            if (BudgetTypeText != null) BudgetTypeText.Text = b.TypeDisplay;
            if (BudgetPeriodText != null) BudgetPeriodText.Text = b.Period;

            if (BudgetPlannedText != null) BudgetPlannedText.Text = $"{b.PlannedAmount:N2} zł";
            if (BudgetSpentText != null) BudgetSpentText.Text = $"{b.SpentAmount:N2} zł";
            if (BudgetOverText != null) BudgetOverText.Text = b.IsOverBudget ? $"{b.OverAmount:N2} zł" : "0,00 zł";

            if (BudgetMetaText != null) BudgetMetaText.Text = $"{b.TypeDisplay} | {b.Period}";

            LoadBudgetHistoryChart(b);
            if (BudgetInsightsList != null) BudgetInsightsList.ItemsSource = BuildInsights(b);
        }

        private IList<string> BuildInsights(BudgetRow b)
        {
            var list = new List<string>();

            var today = DateTime.Today;
            var start = b.StartDate.Date;
            var end = b.EndDate.Date;

            var totalDays = Math.Max(1, (end - start).Days + 1);
            var daysPassed = today < start ? 0 : Math.Min(totalDays, (today - start).Days + 1);
            var daysLeft = Math.Max(0, totalDays - daysPassed);

            var totalBudget = b.PlannedAmount + b.IncomeAmount;
            var avgSpentPerDay = daysPassed <= 0 ? 0m : b.SpentAmount / daysPassed;

            list.Add($"Okres: {daysPassed}/{totalDays} dni (pozostało: {daysLeft} dni).");
            list.Add($"Średnie wydatki dziennie: {avgSpentPerDay:N2} zł.");

            if (daysLeft > 0)
            {
                var safePerDay = b.RemainingAmount / daysLeft;
                list.Add($"Aby się zmieścić: maks. {safePerDay:N2} zł/dzień do końca.");
            }

            if (totalBudget > 0)
            {
                var usedPct = (double)(b.SpentAmount / totalBudget) * 100.0;
                list.Add($"Wykorzystanie budżetu: {usedPct:0}%.");
            }

            if (b.IsOverBudget)
                list.Add($"Przekroczono o: {b.OverAmount:N2} zł.");
            else
                list.Add($"Bufor (na dziś): {b.RemainingAmount:N2} zł.");

            if (daysPassed <= 0 || b.SpentAmount <= 0)
            {
                list.Add("Prognoza na koniec: Brak danych.");
                return list;
            }

            var forecastSpent = avgSpentPerDay * totalDays;
            var forecastRemaining = totalBudget - forecastSpent;

            if (forecastRemaining < 0)
                list.Add($"Prognoza na koniec: przekroczysz o {Math.Abs(forecastRemaining):N2} zł.");
            else
                list.Add($"Prognoza na koniec: zostanie ok. {forecastRemaining:N2} zł.");

            return list;
        }

        // =================== WYKRES (historia) ===================

        private void LoadBudgetHistoryChart(BudgetRow? budget)
        {
            if (BudgetHistoryChartControl == null)
                return;

            if (budget == null)
            {
                BudgetHistoryChartControl.Series = Array.Empty<ISeries>();
                return;
            }

            var daily = new SortedDictionary<DateTime, (decimal income, decimal expense)>();

            using var con = DatabaseService.GetConnection();
            con.Open();

            // PRZYCHODY
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Date, IFNULL(SUM(Amount), 0) AS Total
FROM Incomes
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) BETWEEN date(@from) AND date(@to)
GROUP BY Date
ORDER BY Date;";
                cmd.Parameters.AddWithValue("@uid", _currentUserId);
                cmd.Parameters.AddWithValue("@bid", budget.Id);
                cmd.Parameters.AddWithValue("@from", budget.StartDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", budget.EndDate.ToString("yyyy-MM-dd"));

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var dateStr = reader.GetString(0);
                    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);

                    var total = ReadDecimal(reader, "Total");

                    if (!daily.TryGetValue(date, out var tuple))
                        tuple = (0m, 0m);

                    tuple.income += total;
                    daily[date] = tuple;
                }
            }

            // WYDATKI
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Date, IFNULL(SUM(Amount), 0) AS Total
FROM Expenses
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) BETWEEN date(@from) AND date(@to)
GROUP BY Date
ORDER BY Date;";
                cmd.Parameters.AddWithValue("@uid", _currentUserId);
                cmd.Parameters.AddWithValue("@bid", budget.Id);
                cmd.Parameters.AddWithValue("@from", budget.StartDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", budget.EndDate.ToString("yyyy-MM-dd"));

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var dateStr = reader.GetString(0);
                    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);

                    var total = ReadDecimal(reader, "Total");

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

            var incomeValues = new List<double>();
            var expenseValues = new List<double>();

            decimal cumIncome = 0m;
            decimal cumExpense = 0m;

            foreach (var kvp in daily)
            {
                cumIncome += kvp.Value.income;
                cumExpense += kvp.Value.expense;

                incomeValues.Add((double)cumIncome);
                expenseValues.Add((double)cumExpense);
            }

            BudgetHistoryChartControl.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "Przychody (narastająco)",
                    Values = incomeValues,
                    GeometrySize = 5
                },
                new LineSeries<double>
                {
                    Name = "Wydatki (narastająco)",
                    Values = expenseValues,
                    GeometrySize = 5
                }
            };
        }

        // =================== CRUD ===================

        private void AddBudget_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => AddBudget_Click(sender, new RoutedEventArgs());

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
            vm.Type = ToDbType(vm.Type);

            var newId = BudgetService.InsertBudget(_currentUserId, vm);
            ReloadAndReselect(newId);
        }

        private void EditBudget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not BudgetRow row)
                return;

            var dialog = new Views.Dialogs.EditBudgetDialog
            {
                Owner = Window.GetWindow(this)
            };

            dialog.LoadBudget(row);

            var result = dialog.ShowDialog();
            if (result != true || dialog.Budget == null)
                return;

            var vm = dialog.Budget;
            vm.Type = ToDbType(vm.Type);

            var updated = new Budget
            {
                Id = row.Id,
                UserId = _currentUserId,
                Name = vm.Name,
                Type = vm.Type,
                StartDate = (vm.StartDate ?? row.StartDate).Date,
                EndDate = (vm.EndDate ?? row.EndDate).Date,
                PlannedAmount = vm.PlannedAmount
            };

            BudgetService.UpdateBudget(updated);
            ReloadAndReselect(row.Id);
        }

        // ======= INLINE DELETE =======

        private void HideAllDeleteConfirms()
        {
            foreach (var b in _allBudgets)
                b.IsDeleteConfirmVisible = false;
        }

        private void DeleteBudget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not BudgetRow row)
                return;

            HideAllDeleteConfirms();
            row.IsDeleteConfirmVisible = true;
        }

        private void DeleteBudgetConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not BudgetRow row)
                return;

            row.IsDeleteConfirmVisible = false;
        }

        private void DeleteBudgetConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not BudgetRow row)
                return;

            BudgetService.DeleteBudget(row.Id, _currentUserId);

            HideAllDeleteConfirms();
            ReloadAndReselect(null);
        }

        private void ReloadAndReselect(int? preferredId)
        {
            LoadBudgetsFromDatabase();
            ApplyFilters();
            UpdateTopKpis();

            var list = BudgetsList?.ItemsSource as IEnumerable<BudgetRow>;
            if (list == null)
            {
                UpdateDetailsPanel(null);
                LoadBudgetTransactions(null);
                return;
            }

            BudgetRow? select = null;

            if (preferredId.HasValue)
                select = list.FirstOrDefault(x => x.Id == preferredId.Value);

            select ??= list.FirstOrDefault();

            if (BudgetsList != null)
                BudgetsList.SelectedItem = select;

            UpdateDetailsPanel(select);
            LoadBudgetTransactions(select);
        }
    }

    // =================== MODEL UI ===================

    public class BudgetRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isDeleteConfirmVisible;
        public bool IsDeleteConfirmVisible
        {
            get => _isDeleteConfirmVisible;
            set
            {
                if (_isDeleteConfirmVisible == value) return;
                _isDeleteConfirmVisible = value;
                OnPropertyChanged();
            }
        }

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // DB: Weekly/Monthly/Yearly/Custom
        public string Type { get; set; } = "Monthly";

        // UI: Tygodniowy/Miesięczny/Roczny/Inny
        public string TypeDisplay { get; set; } = "Miesięczny";

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public string Period => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}";

        public decimal PlannedAmount { get; set; }
        public decimal SpentAmount { get; set; }
        public decimal IncomeAmount { get; set; }

        public decimal RemainingAmount { get; set; }

        public int OverState { get; set; }
        public string? OverNotifiedAt { get; set; }

        public bool IsOverBudget => RemainingAmount < 0;
        public decimal OverAmount => IsOverBudget ? Math.Abs(RemainingAmount) : 0m;

        public decimal OverDisplayAmount => OverAmount;

        public void Recalculate()
        {
            var total = PlannedAmount + IncomeAmount;
            RemainingAmount = total - SpentAmount;
        }

        public override string ToString() => Name;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
