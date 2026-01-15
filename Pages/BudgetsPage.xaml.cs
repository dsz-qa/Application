using Finly.Models;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private readonly ObservableCollection<BudgetRow> _allBudgets = new();
        private readonly int _currentUserId;

        private bool _initializedOk;
        private bool _isActive;
        private bool _isInitializedOnce;
        private bool _dataChangedHooked;
        private long _reloadToken;

        public BudgetsPage(int userId)
        {
            try
            {
                InitializeComponent();
                _initializedOk = true;

                _currentUserId = userId;

                Loaded += BudgetsPage_Loaded;
                Unloaded += BudgetsPage_Unloaded;
            }
            catch (Exception ex)
            {
                _initializedOk = false;
                Content = new TextBlock
                {
                    Text = "Nie można załadować strony Budżety:\n" + ex,
                    Foreground = Brushes.Tomato,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16)
                };
            }
        }

        public BudgetsPage() : this(UserService.CurrentUserId) { }

        // =================== LIFECYCLE ===================

        private void BudgetsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_initializedOk) return;

            _isActive = true;
            _reloadToken++;

            HookDataChanged();

            if (_isInitializedOnce)
            {
                SafeReloadKeepSelection();
                return;
            }

            _isInitializedOnce = true;

            try { InitTypeFilterCombo(); } catch { }

            SafeReloadKeepSelection();

            try
            {
                if (BudgetsList != null && BudgetsList.SelectedItem == null && BudgetsList.Items.Count > 0)
                    BudgetsList.SelectedIndex = 0;
            }
            catch { }

            try
            {
                var selected = GetSelectedBudgetFromList();
                UpdateDetailsPanel(selected);
                LoadBudgetTransactions(selected);
            }
            catch { }
        }

        private void BudgetsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isActive = false;
            _reloadToken++;

            UnhookDataChanged();

            try { ResetChartSafe("Wybierz budżet, aby zobaczyć historię."); } catch { }

            try
            {
                if (BudgetTransactionsList != null) BudgetTransactionsList.ItemsSource = null;
                if (BudgetTransactionsHintText != null)
                    BudgetTransactionsHintText.Text = "Wybierz budżet z listy po lewej, aby zobaczyć jego transakcje.";
            }
            catch { }
        }

        private void HookDataChanged()
        {
            if (_dataChangedHooked) return;

            try
            {
                DatabaseService.DataChanged += DatabaseService_DataChanged;
                _dataChangedHooked = true;
            }
            catch
            {
                _dataChangedHooked = false;
            }
        }

        private void UnhookDataChanged()
        {
            if (!_dataChangedHooked) return;

            try { DatabaseService.DataChanged -= DatabaseService_DataChanged; }
            catch { }

            _dataChangedHooked = false;
        }

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            if (!_isActive) return;

            var tokenSnapshot = _reloadToken;

            try
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isActive) return;
                    if (tokenSnapshot != _reloadToken) return;
                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                    SafeReloadKeepSelection();
                }), DispatcherPriority.Background);
            }
            catch { }
        }

        private void SafeReloadKeepSelection()
        {
            if (!_isActive) return;

            int? selectedId = null;
            try { selectedId = (BudgetsList?.SelectedItem as BudgetRow)?.Id; } catch { }

            try
            {
                LoadBudgetsFromDatabase();
                ApplyFilters();
                UpdateTopKpis();

                // restore selection
                try
                {
                    if (BudgetsList != null && BudgetsList.ItemsSource is IEnumerable<BudgetRow> list)
                    {
                        BudgetRow? select = null;

                        if (selectedId.HasValue)
                            select = list.FirstOrDefault(x => x.Id == selectedId.Value);

                        select ??= list.FirstOrDefault();
                        BudgetsList.SelectedItem = select;
                    }
                }
                catch { }

                try
                {
                    var selected = GetSelectedBudgetFromList();
                    UpdateDetailsPanel(selected);
                    LoadBudgetTransactions(selected);
                }
                catch { }
            }
            catch (Exception ex)
            {
                try
                {
                    ResetChartSafe("Nie udało się wczytać historii budżetu.");
                    if (BudgetInsightsList != null) BudgetInsightsList.ItemsSource = new[] { "Brak danych." };
                }
                catch { }

                System.Diagnostics.Debug.WriteLine("BudgetsPage reload error: " + ex);
            }
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

            return "Monthly";
        }

        // =================== DB ===================

        private void LoadBudgetsFromDatabase()
        {
            _allBudgets.Clear();

            using var conn = DatabaseService.GetConnection();
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

                var startDate = ReadDate(reader, "StartDate");
                var endDate = ReadDate(reader, "EndDate");

                var row = new BudgetRow
                {
                    Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
                    Name = reader["Name"]?.ToString() ?? string.Empty,

                    Type = normalizedType,
                    TypeDisplay = ToPlType(normalizedType),

                    StartDate = startDate,
                    EndDate = endDate,

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

        private static DateTime ReadDate(IDataRecord reader, string column)
        {
            try
            {
                var ordinal = reader.GetOrdinal(column);
                if (reader.IsDBNull(ordinal)) return DateTime.Today;

                var obj = reader.GetValue(ordinal);

                if (obj is DateTime dt) return dt;
                if (obj is string s)
                {
                    if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                        return parsed;
                    if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
                        return parsed;
                }

                return Convert.ToDateTime(obj, CultureInfo.InvariantCulture);
            }
            catch
            {
                return DateTime.Today;
            }
        }

        private static decimal ReadDecimal(IDataRecord reader, string column)
        {
            try
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
            catch
            {
                return 0m;
            }
        }

        // =================== LIVE FILTERY ===================

        private void TypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilters();
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (BudgetsList == null || TypeFilterCombo == null || SearchBox == null)
                return;

            int? previouslySelectedId = null;
            try { previouslySelectedId = (BudgetsList.SelectedItem as BudgetRow)?.Id; } catch { }

            var typeValue = "";
            try { typeValue = TypeFilterCombo.SelectedValue as string ?? ""; } catch { }

            IEnumerable<BudgetRow> query = _allBudgets;

            if (!string.IsNullOrWhiteSpace(typeValue))
            {
                query = query.Where(b =>
                    string.Equals(b.Type, typeValue, StringComparison.OrdinalIgnoreCase));
            }

            var search = "";
            try { search = (SearchBox.Text ?? string.Empty).Trim(); } catch { }

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
            try
            {
                HideAllDeleteConfirms();

                var selected = GetSelectedBudgetFromList();
                UpdateDetailsPanel(selected);
                LoadBudgetTransactions(selected);
            }
            catch { }
        }

        // =================== PRAWA LISTA: TRANSAKCJE ===================

        private void LoadBudgetTransactions(BudgetRow? budget)
        {
            if (BudgetTransactionsList == null || BudgetTransactionsHintText == null)
                return;

            try
            {
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
            catch (Exception ex)
            {
                BudgetTransactionsList.ItemsSource = null;
                BudgetTransactionsHintText.Text = "Nie udało się wczytać transakcji budżetu.";
                System.Diagnostics.Debug.WriteLine("LoadBudgetTransactions error: " + ex);
            }
        }

        // =================== KPI OGÓLNE (góra) ===================

        private void UpdateTopKpis()
        {
            try
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
            catch { }
        }

        // =================== PANEL (kafelki u góry) ===================

        private void UpdateDetailsPanel(BudgetRow? b)
        {
            if (BudgetNameText == null) return;

            try
            {
                if (b == null)
                {
                    BudgetNameText.Text = "(wybierz budżet)";
                    if (BudgetTypeText != null) BudgetTypeText.Text = "—";
                    if (BudgetPeriodText != null) BudgetPeriodText.Text = "—";
                    if (BudgetPlannedText != null) BudgetPlannedText.Text = "0,00 zł";
                    if (BudgetSpentText != null) BudgetSpentText.Text = "0,00 zł";
                    if (BudgetOverText != null) BudgetOverText.Text = "0,00 zł";
                    if (BudgetMetaText != null) BudgetMetaText.Text = string.Empty;

                    if (BudgetProgressBar != null)
                    {
                        BudgetProgressBar.Value = 0;
                        BudgetProgressBar.Foreground = Brushes.LimeGreen;
                    }
                    if (BudgetProgressText != null) BudgetProgressText.Text = "0%";

                    ResetChartSafe("Wybierz budżet, aby zobaczyć historię.");
                    if (BudgetInsightsList != null) BudgetInsightsList.ItemsSource = new[] { "Brak danych." };
                    return;
                }

                b.Recalculate();

                BudgetNameText.Text = b.Name;
                if (BudgetTypeText != null) BudgetTypeText.Text = b.TypeDisplay;
                if (BudgetPeriodText != null) BudgetPeriodText.Text = b.Period;

                if (BudgetPlannedText != null) BudgetPlannedText.Text = $"{b.PlannedAmount:N2} zł";
                if (BudgetSpentText != null) BudgetSpentText.Text = $"{b.SpentAmount:N2} zł";
                if (BudgetOverText != null) BudgetOverText.Text = b.IsOverBudget ? $"{b.OverAmount:N2} zł" : "0,00 zł";
                if (BudgetMetaText != null) BudgetMetaText.Text = $"{b.TypeDisplay} | {b.Period}";

                var totalBudget = b.PlannedAmount + b.IncomeAmount;

                double pct = 0;
                if (totalBudget > 0)
                    pct = (double)(b.SpentAmount / totalBudget * 100m);

                if (pct < 0) pct = 0;

                var isOver = b.IsOverBudget;
                var clamped = Math.Max(0, Math.Min(100, pct));

                if (BudgetProgressBar != null)
                {
                    BudgetProgressBar.Value = isOver ? 100 : clamped;

                    if (isOver || pct >= 100)
                        BudgetProgressBar.Foreground = Brushes.Tomato;
                    else if (pct >= 80)
                        BudgetProgressBar.Foreground = Brushes.Orange;
                    else
                        BudgetProgressBar.Foreground = Brushes.LimeGreen;
                }

                if (BudgetProgressText != null)
                {
                    if (pct >= 100.0)
                    {
                        var overPct = pct - 100.0;
                        BudgetProgressText.Text = $"{Math.Round(pct):0}% (+{Math.Round(overPct):0}%)";
                    }
                    else
                    {
                        BudgetProgressText.Text = $"{Math.Round(pct):0}%";
                    }
                }

                LoadBudgetHistoryChartSafe(b);
                if (BudgetInsightsList != null) BudgetInsightsList.ItemsSource = BuildInsights(b);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("UpdateDetailsPanel error: " + ex);
                ResetChartSafe("Nie udało się odświeżyć szczegółów budżetu.");
                if (BudgetInsightsList != null) BudgetInsightsList.ItemsSource = new[] { "Brak danych." };
            }
        }

        private IList<string> BuildInsights(BudgetRow b)
        {
            try
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
            catch
            {
                return new List<string> { "Brak danych." };
            }
        }

        // =================== WYKRES (historia) ===================

        private void ResetChartSafe(string hint)
        {
            try
            {
                if (BudgetHistoryChartControl != null)
                {
                    BudgetHistoryChartControl.Series = Array.Empty<ISeries>();
                    BudgetHistoryChartControl.XAxes = Array.Empty<Axis>();
                    BudgetHistoryChartControl.YAxes = Array.Empty<Axis>();
                }

                if (BudgetHistoryChartErrorText != null)
                {
                    BudgetHistoryChartErrorText.Text = "";
                    BudgetHistoryChartErrorText.Visibility = Visibility.Collapsed;
                }

                if (BudgetHistoryChartHintText != null)
                {
                    BudgetHistoryChartHintText.Text = hint;
                    BudgetHistoryChartHintText.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 10;

            var exp = Math.Floor(Math.Log10(raw));
            var baseVal = Math.Pow(10, exp);
            var f = raw / baseVal;

            double nice;
            if (f <= 1) nice = 1;
            else if (f <= 2) nice = 2;
            else if (f <= 5) nice = 5;
            else nice = 10;

            return nice * baseVal;
        }


        private void SetChartError(string message)
        {
            try
            {
                if (BudgetHistoryChartHintText != null)
                    BudgetHistoryChartHintText.Visibility = Visibility.Collapsed;

                if (BudgetHistoryChartErrorText != null)
                {
                    BudgetHistoryChartErrorText.Text = message;
                    BudgetHistoryChartErrorText.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private void LoadBudgetHistoryChartSafe(BudgetRow budget)
        {
            if (BudgetHistoryChartControl == null)
                return;

            try
            {
                var from = budget.StartDate.Date;
                var to = budget.EndDate.Date;
                if (to < from)
                {
                    ResetChartSafe("Nieprawidłowy zakres dat budżetu.");
                    return;
                }

                var daily = new SortedDictionary<DateTime, (decimal income, decimal expense)>();

                using var con = DatabaseService.GetConnection();

                // incomes
                try
                {
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = @"
SELECT date(Date) as Day, IFNULL(SUM(Amount), 0) AS Total
FROM Incomes
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) BETWEEN date(@from) AND date(@to)
GROUP BY date(Date)
ORDER BY date(Date);";
                    cmd.Parameters.AddWithValue("@uid", _currentUserId);
                    cmd.Parameters.AddWithValue("@bid", budget.Id);
                    cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var dayStr = reader.GetString(0);
                        if (!DateTime.TryParseExact(dayStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            continue;

                        var total = ReadDecimal(reader, "Total");
                        if (!daily.TryGetValue(d, out var tuple)) tuple = (0m, 0m);
                        tuple.income += total;
                        daily[d] = tuple;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Budget chart incomes error: " + ex);
                }

                // expenses
                try
                {
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = @"
SELECT date(Date) as Day, IFNULL(SUM(Amount), 0) AS Total
FROM Expenses
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) BETWEEN date(@from) AND date(@to)
GROUP BY date(Date)
ORDER BY date(Date);";
                    cmd.Parameters.AddWithValue("@uid", _currentUserId);
                    cmd.Parameters.AddWithValue("@bid", budget.Id);
                    cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var dayStr = reader.GetString(0);
                        if (!DateTime.TryParseExact(dayStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            continue;

                        var total = ReadDecimal(reader, "Total");
                        if (!daily.TryGetValue(d, out var tuple)) tuple = (0m, 0m);
                        tuple.expense += total;
                        daily[d] = tuple;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Budget chart expenses error: " + ex);
                }

                for (var d = from; d <= to; d = d.AddDays(1))
                    if (!daily.ContainsKey(d))
                        daily[d] = (0m, 0m);

                var labels = new List<string>();
                var cumSpentValues = new List<double>();
                var limitValues = new List<double>();
                var remainingValues = new List<double>();

                var limit = budget.PlannedAmount + budget.IncomeAmount;
                decimal cumSpent = 0m;

                foreach (var kvp in daily)
                {
                    var day = kvp.Key;
                    var (_, exp) = kvp.Value;

                    cumSpent += exp;

                    labels.Add(day.ToString("dd.MM", CultureInfo.InvariantCulture));
                    cumSpentValues.Add((double)cumSpent);
                    limitValues.Add((double)limit);
                    remainingValues.Add((double)(limit - cumSpent));
                }

                if (labels.Count == 0)
                {
                    ResetChartSafe("Brak danych do wykresu.");
                    return;
                }

                try
                {
                    if (BudgetHistoryChartErrorText != null)
                    {
                        BudgetHistoryChartErrorText.Text = "";
                        BudgetHistoryChartErrorText.Visibility = Visibility.Collapsed;
                    }
                    if (BudgetHistoryChartHintText != null)
                    {
                        BudgetHistoryChartHintText.Text = "Historia wydatków w czasie (narastająco).";
                        BudgetHistoryChartHintText.Visibility = Visibility.Visible;
                    }
                }
                catch { }

                var whitePaint = new SolidColorPaint(SKColors.White);

                BudgetHistoryChartControl.XAxes = new Axis[]
                {
                    new Axis
                    {
                        Labels = labels,
                        LabelsRotation = 0,
                        SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 35)),
                        TextSize = 12,
                        LabelsPaint = whitePaint,
                        NamePaint = whitePaint,
                        TicksPaint = whitePaint
                    }
                };

                var lastRemaining = remainingValues.LastOrDefault();

                var maxSpent = cumSpentValues.Count == 0 ? 0 : cumSpentValues.Max();
                var maxLimit = limitValues.Count == 0 ? 0 : limitValues.Max();
                var maxY = Math.Max(maxSpent, maxLimit);

                // chcemy ~4-5 podziałek
                var step = NiceStep(maxY / 4.0);
                if (step <= 0) step = 10;

                // zaokrąglamy max do pełnego kroku (żeby ticki były „ładne”)
                var maxAxis = Math.Ceiling(maxY / step) * step;
                if (maxAxis < step) maxAxis = step;

                BudgetHistoryChartControl.YAxes = new Axis[]
                {
    new Axis
    {
        MinLimit = 0,
        MaxLimit = maxAxis,
        MinStep = step,
        ForceStepToMin = true,

        SeparatorsPaint = null, // możesz zmienić na delikatne kreski, jeśli chcesz
        Labeler = v => $"{v:N0} zł",
        TextSize = 12,
        LabelsPaint = whitePaint,
        NamePaint = whitePaint,
        TicksPaint = whitePaint
    }
                };


                var spentSeries = new LineSeries<double>
                {
                    Name = "Wydatki narastająco",
                    Values = cumSpentValues,
                    GeometrySize = 4,
                    Fill = null
                };

                var limitSeries = new LineSeries<double>
                {
                    Name = "Limit budżetu",
                    Values = limitValues,
                    GeometrySize = 0,
                    Fill = null
                };

                var remainingSeries = new LineSeries<double>
                {
                    Name = "Pozostało",
                    Values = remainingValues,
                    GeometrySize = 0,
                    Fill = null
                };

                var lastIndex = remainingValues.Count - 1;

                var lastSpent = cumSpentValues.LastOrDefault();

                var spentLastPointSeries = new ScatterSeries<ObservablePoint>
                {
                    Name = "",
                    IsVisibleAtLegend = false,
                    GeometrySize = 10,
                    Values = new[] { new ObservablePoint(lastIndex, lastSpent) },
                    DataLabelsPaint = whitePaint,
                    DataLabelsSize = 12,
                    DataLabelsPosition = DataLabelsPosition.Top,
                    DataLabelsFormatter = p => $"{p.Coordinate.PrimaryValue:N0} zł"
                };


                var remainingLastPointSeries = new ScatterSeries<ObservablePoint>
                {
                    Name = "",
                    IsVisibleAtLegend = false,
                    GeometrySize = 10,
                    Values = new[] { new ObservablePoint(lastIndex, lastRemaining) },
                    DataLabelsPaint = whitePaint,
                    DataLabelsSize = 12,
                    DataLabelsPosition = DataLabelsPosition.Top,
                    DataLabelsFormatter = p => $"{p.Coordinate.PrimaryValue:N0} zł"
                };

                BudgetHistoryChartControl.LegendTextPaint = whitePaint;
                BudgetHistoryChartControl.Series = new ISeries[]
                {
    spentSeries,
    limitSeries,
    remainingSeries,
    spentLastPointSeries,
    remainingLastPointSeries
                };

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadBudgetHistoryChartSafe fatal error: " + ex);
                ResetChartSafe("Wykres niedostępny.");
                SetChartError("Nie udało się wygenerować wykresu historii (błąd biblioteki wykresów).");
            }
        }

        // =================== CRUD ===================

        private void AddBudget_Click(object sender, MouseButtonEventArgs e)
            => AddBudget_Click(sender, new RoutedEventArgs());

        private void AddBudget_Click(object sender, RoutedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AddBudget_Click error: " + ex);
            }
        }

        private void EditBudget_Click(object sender, RoutedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EditBudget_Click error: " + ex);
            }
        }

        // ======= INLINE DELETE =======

        private void HideAllDeleteConfirms()
        {
            try
            {
                foreach (var b in _allBudgets)
                    b.IsDeleteConfirmVisible = false;
            }
            catch { }
        }

        private void DeleteBudget_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement fe || fe.DataContext is not BudgetRow row)
                    return;

                HideAllDeleteConfirms();
                row.IsDeleteConfirmVisible = true;
            }
            catch { }
        }

        private void DeleteBudgetConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement fe || fe.DataContext is not BudgetRow row)
                    return;

                row.IsDeleteConfirmVisible = false;
            }
            catch { }
        }

        private void DeleteBudgetConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement fe || fe.DataContext is not BudgetRow row)
                    return;

                BudgetService.DeleteBudget(row.Id, _currentUserId);

                HideAllDeleteConfirms();
                ReloadAndReselect(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DeleteBudgetConfirmYes_Click error: " + ex);
            }
        }

        private void ReloadAndReselect(int? preferredId)
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ReloadAndReselect error: " + ex);
                ResetChartSafe("Nie udało się odświeżyć danych.");
            }
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

        public string Type { get; set; } = "Monthly";
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
            try
            {
                var total = PlannedAmount + IncomeAmount;
                RemainingAmount = total - SpentAmount;
            }
            catch
            {
                RemainingAmount = 0m;
            }
        }

        public override string ToString() => Name;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // =================== KONWERTER: string -> Visibility ===================

    public sealed class StringNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            var hasText = !string.IsNullOrWhiteSpace(s);

            if (Invert) hasText = !hasText;
            return hasText ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }


    // =================== KONWERTER: ProgressBar fill (Value -> Width) ===================

    public sealed class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 4) return 0d;

                var actualWidth = values[0] is double aw ? aw : 0d;
                var value = values[1] is double v ? v : 0d;
                var max = values[2] is double mx ? mx : 100d;
                var min = values[3] is double mn ? mn : 0d;

                var denom = (max - min);
                if (actualWidth <= 0 || denom <= 0) return 0d;

                var t = (value - min) / denom;
                if (double.IsNaN(t) || double.IsInfinity(t)) t = 0;

                t = Math.Max(0, Math.Min(1, t));
                return actualWidth * t;
            }
            catch
            {
                return 0d;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => new object[] { Binding.DoNothing, Binding.DoNothing, Binding.DoNothing, Binding.DoNothing };
    }
}
