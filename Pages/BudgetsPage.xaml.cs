using Finly.Models;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

            InitTypeFilterCombo();

            LoadBudgetsFromDatabase();
            ApplyFilters();
            UpdateTopKpis();

            Loaded += (_, __) =>
            {
                if (BudgetsList.SelectedItem == null && BudgetsList.Items.Count > 0)
                    BudgetsList.SelectedIndex = 0;

                UpdateDetailsPanel(GetSelectedBudgetFromList());
            };
        }

        public BudgetsPage() : this(UserService.CurrentUserId) { }

        // =================== TYPY ===================

        private void InitTypeFilterCombo()
        {
            TypeFilterCombo.Items.Clear();

            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Wszystkie", Tag = "Wszystkie" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Tygodniowy", Tag = "Weekly" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Miesięczny", Tag = "Monthly" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Roczny", Tag = "Yearly" });

            TypeFilterCombo.SelectedIndex = 0;
        }

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

            if (string.Equals(s, "Tygodniowy", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (string.Equals(s, "Miesięczny", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (string.Equals(s, "Roczny", StringComparison.OrdinalIgnoreCase)) return "Yearly";

            if (string.Equals(s, "Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (string.Equals(s, "Monthly", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (string.Equals(s, "Yearly", StringComparison.OrdinalIgnoreCase)) return "Yearly";

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
                var rawType = reader["Type"]?.ToString() ?? "Monthly";

                var row = new BudgetRow
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"]?.ToString() ?? string.Empty,

                    Type = rawType,
                    TypeDisplay = ToPlType(rawType),

                    StartDate = Convert.ToDateTime(reader["StartDate"]),
                    EndDate = Convert.ToDateTime(reader["EndDate"]),

                    PlannedAmount = ReadDecimal(reader, "PlannedAmount"),
                    SpentAmount = ReadDecimal(reader, "SpentAmount"),
                    IncomeAmount = ReadDecimal(reader, "IncomeAmount"),

                    OverState = reader["OverState"] == DBNull.Value ? 0 : Convert.ToInt32(reader["OverState"]),
                    OverNotifiedAt = reader["OverNotifiedAt"] == DBNull.Value ? null : reader["OverNotifiedAt"].ToString(),
                };

                row.Recalculate();
                _allBudgets.Add(row);
            }
        }

        private static decimal ReadDecimal(System.Data.IDataRecord reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return 0m;

            // SQLite potrafi zwrócić double nawet jeśli logicznie to decimal
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
            // zapamiętaj ID selekcji zanim podmienisz ItemsSource
            var previouslySelectedId = (BudgetsList.SelectedItem as BudgetRow)?.Id;

            var selectedTypeItem = TypeFilterCombo.SelectedItem as ComboBoxItem;
            var typeValue = selectedTypeItem?.Tag as string ?? "Wszystkie";

            IEnumerable<BudgetRow> query = _allBudgets;

            if (!string.IsNullOrWhiteSpace(typeValue) &&
                !string.Equals(typeValue, "Wszystkie", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(b => string.Equals(b.Type, typeValue, StringComparison.OrdinalIgnoreCase));
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
                return;
            }

            // spróbuj zachować poprzednią selekcję po ID
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

        private BudgetRow? GetSelectedBudgetFromList() => BudgetsList.SelectedItem as BudgetRow;

        private void BudgetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = GetSelectedBudgetFromList();
            UpdateDetailsPanel(selected);

            // TODO: tutaj docelowo doładujesz transakcje pod tabelą:
            // LoadBudgetTransactions(selected);
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

            KpiActiveBudgetsText.Text = activeCount.ToString(CultureInfo.InvariantCulture);
            KpiPlannedActiveText.Text = $"{planned:N2} zł";
            KpiOverActiveText.Text = $"{overCount} | {overAmount:N2} zł";
        }

        // =================== PANEL PRAWA STRONA ===================

        private void UpdateDetailsPanel(BudgetRow? b)
        {
            if (b == null)
            {
                BudgetNameText.Text = "(wybierz budżet)";
                BudgetMetaText.Text = string.Empty;

                // w nowym UI po prawej pokazujemy tylko limit
                BudgetPlannedText.Text = "0,00 zł";

                LoadBudgetHistoryChart(null);
                BudgetInsightsList.ItemsSource = Array.Empty<string>();
                return;
            }

            BudgetNameText.Text = b.Name;
            BudgetMetaText.Text = $"{b.TypeDisplay} | {b.Period}";

            // limit (planowane)
            BudgetPlannedText.Text = $"{b.PlannedAmount:N2} zł";

            LoadBudgetHistoryChart(b);
            BudgetInsightsList.ItemsSource = BuildInsights(b);
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
                list.Add($"Uwaga: budżet przekroczony o {b.OverAmount:N2} zł.");
            else
                list.Add($"Bufor: {b.RemainingAmount:N2} zł.");

            if (daysPassed > 0)
            {
                var forecastSpent = avgSpentPerDay * totalDays;
                var forecastRemaining = (b.PlannedAmount + b.IncomeAmount) - forecastSpent;

                if (forecastRemaining < 0)
                    list.Add($"Prognoza: przy tym tempie przekroczysz o {Math.Abs(forecastRemaining):N2} zł.");
                else
                    list.Add($"Prognoza: przy tym tempie zostanie ok. {forecastRemaining:N2} zł.");
            }

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

                    var totalObj = reader.GetValue(1);
                    var total = totalObj switch
                    {
                        double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
                        decimal dec => dec,
                        long l => l,
                        int i => i,
                        _ => Convert.ToDecimal(totalObj, CultureInfo.InvariantCulture)
                    };

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

                    var totalObj = reader.GetValue(1);
                    var total = totalObj switch
                    {
                        double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
                        decimal dec => dec,
                        long l => l,
                        int i => i,
                        _ => Convert.ToDecimal(totalObj, CultureInfo.InvariantCulture)
                    };

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

            BudgetService.InsertBudget(_currentUserId, vm);

            LoadBudgetsFromDatabase();
            ApplyFilters();
            UpdateTopKpis();

            // po dodaniu: spróbuj zaznaczyć nowo dodany (po nazwie+starcie)
            var inserted = (_allBudgets.OrderByDescending(b => b.StartDate).FirstOrDefault());
            if (inserted != null)
                BudgetsList.SelectedItem = (BudgetsList.ItemsSource as IEnumerable<BudgetRow>)?.FirstOrDefault(x => x.Id == inserted.Id);
        }
    }

    // =================== MODEL UI ===================

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

        public int OverState { get; set; }
        public string? OverNotifiedAt { get; set; }

        public bool IsOverBudget => RemainingAmount < 0;
        public decimal OverAmount => IsOverBudget ? Math.Abs(RemainingAmount) : 0m;

        public void Recalculate()
        {
            var total = PlannedAmount + IncomeAmount;
            RemainingAmount = total - SpentAmount;
        }

        public override string ToString() => Name;
    }
}
