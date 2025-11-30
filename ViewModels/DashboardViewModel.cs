using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Finly.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Microsoft.Data.Sqlite;

namespace Finly.ViewModels
{
 public sealed class ForecastModel
 {
 public decimal PredictedSpending { get; set; }
 public decimal PredictedFreeCash { get; set; }
 }

 public sealed class TransactionItem
 {
 public int Id { get; set; }
 public DateTime Date { get; set; }
 public string DateDisplay => Date.ToString("d");
 public string Category { get; set; } = string.Empty;
 public string Account { get; set; } = string.Empty; // sformatowane Ÿród³o
 public string Description { get; set; } = string.Empty;
 public string Kind { get; set; } = string.Empty; // Przychód/Wydatek/Transfer
 public decimal Amount { get; set; }
 public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " z³";
 public string RawSource { get; set; } = string.Empty; // oryginalny tekst Ÿród³a z DB
 public void NormalizeAccount()
 {
 if (string.IsNullOrWhiteSpace(Account) && !string.IsNullOrWhiteSpace(RawSource))
 {
 var s = RawSource.Trim();
 if (s.Equals("Wolna gotówka", StringComparison.OrdinalIgnoreCase)) Account = "Wolna gotówka";
 else if (s.Equals("Od³o¿ona gotówka", StringComparison.OrdinalIgnoreCase)) Account = "Od³o¿ona gotówka";
 else if (s.StartsWith("Konto", StringComparison.OrdinalIgnoreCase)) Account = s; // np. "Konto: mBank"
 else if (s.StartsWith("Koperta", StringComparison.OrdinalIgnoreCase)) Account = s; // np. "Koperta: Jedzenie"
 else Account = s; // fallback
 }
 }
 }

 public class DashboardViewModel : INotifyPropertyChanged
 {
 private readonly int _userId;

 public ObservableCollection<string> Insights { get; } = new();
 public ObservableCollection<string> Alerts { get; } = new();
 public ForecastModel Forecast { get; private set; } = new ForecastModel();

 public ObservableCollection<TransactionItem> Incomes { get; } = new();
 public ObservableCollection<TransactionItem> Expenses { get; } = new();
 public ObservableCollection<TransactionItem> PlannedTransactions { get; } = new();

 // Legacy properties kept for backward compatibility
 private ISeries[] _pieExpenseSeries = Array.Empty<ISeries>();
 public ISeries[] PieExpenseSeries { get => _pieExpenseSeries; private set { _pieExpenseSeries = value; OnPropertyChanged(); } }
 private ISeries[] _pieIncomeSeries = Array.Empty<ISeries>();
 public ISeries[] PieIncomeSeries { get => _pieIncomeSeries; private set { _pieIncomeSeries = value; OnPropertyChanged(); } }

 // New properties bound by XAML
 private ISeries[] _expensesByCategorySeries = Array.Empty<ISeries>();
 public ISeries[] ExpensesByCategorySeries { get => _expensesByCategorySeries; private set { _expensesByCategorySeries = value; OnPropertyChanged(); } }
 private ISeries[] _incomeBySourceSeries = Array.Empty<ISeries>();
 public ISeries[] IncomeBySourceSeries { get => _incomeBySourceSeries; private set { _incomeBySourceSeries = value; OnPropertyChanged(); } }

 public DashboardViewModel(int userId) { _userId = userId; }

 // Palette for pie slices – visible on dark theme
 private static readonly SKColor[] PiePalette = new[]
 {
 SKColor.Parse("#ED7A1A"), // orange
 SKColor.Parse("#3FA7D6"), // blue
 SKColor.Parse("#7BC96F"), // green
 SKColor.Parse("#AF7AC5"), // purple
 SKColor.Parse("#F6BF26"), // yellow
 SKColor.Parse("#56C1A7"), // teal
 SKColor.Parse("#CE6A6B"), // red-ish
 SKColor.Parse("#9AA0A6") // gray
 };

 public void RefreshCharts(DateTime start, DateTime end)
 {
 try
 {
 // Build expense series from loaded transactions (ensure LoadTransactions called before this)
 var expenseGroups = Expenses
 .Where(t => t.Kind == "Wydatek" && t.Amount >0)
 .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "(brak kategorii)" : t.Category)
 .OrderByDescending(g => g.Sum(x => x.Amount))
 .ToList();

 var expSeries = expenseGroups.Select((g, i) => new PieSeries<double>
 {
 Name = g.Key,
 Values = new[] { (double)g.Sum(x => x.Amount) },
 InnerRadius =0, // full pie, not donut
 DataLabelsPaint = new SolidColorPaint(SKColors.White),
 DataLabelsSize =11,
 DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
 // label formatter with currency suffix
 DataLabelsFormatter = point => string.Format(CultureInfo.CurrentCulture, "{0:N2} z³", point.Coordinate.PrimaryValue),
 Fill = new SolidColorPaint(PiePalette[i % PiePalette.Length]),
 Stroke = null
 }).Cast<ISeries>().ToArray();

 // If there are no transactions but aggregated service might still have data, fallback
 if (expSeries.Length ==0)
 {
 var exp = DatabaseService.GetSpendingByCategorySafe(_userId, start, end) ?? new System.Collections.Generic.List<DatabaseService.CategoryAmountDto>();
 expSeries = exp.Select((x, i) => new PieSeries<double>
 {
 Name = string.IsNullOrWhiteSpace(x.Name) ? "(brak kategorii)" : x.Name,
 Values = new[] { (double)Math.Abs(x.Amount) },
 InnerRadius =0,
 DataLabelsPaint = new SolidColorPaint(SKColors.White),
 DataLabelsSize =11,
 DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
 DataLabelsFormatter = point => string.Format(CultureInfo.CurrentCulture, "{0:N2} z³", point.Coordinate.PrimaryValue),
 Fill = new SolidColorPaint(PiePalette[i % PiePalette.Length]),
 Stroke = null
 }).Cast<ISeries>().ToArray();
 }

 ExpensesByCategorySeries = expSeries; // bound property
 PieExpenseSeries = expSeries; // legacy sync

 // Build income series from loaded transactions
 var incomeGroups = Incomes
 .Where(t => t.Kind == "Przychód" && t.Amount >0)
 .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Przychody" : t.Category)
 .OrderByDescending(g => g.Sum(x => x.Amount))
 .ToList();

 var incSeries = incomeGroups.Select((g, i) => new PieSeries<double>
 {
 Name = g.Key,
 Values = new[] { (double)g.Sum(x => x.Amount) },
 InnerRadius =0,
 DataLabelsPaint = new SolidColorPaint(SKColors.White),
 DataLabelsSize =11,
 DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
 DataLabelsFormatter = point => string.Format(CultureInfo.CurrentCulture, "{0:N2} z³", point.Coordinate.PrimaryValue),
 Fill = new SolidColorPaint(PiePalette[i % PiePalette.Length]),
 Stroke = null
 }).Cast<ISeries>().ToArray();

 if (incSeries.Length ==0)
 {
 var inc = DatabaseService.GetIncomeBySourceSafe(_userId, start, end) ?? new System.Collections.Generic.List<DatabaseService.CategoryAmountDto>();
 incSeries = inc.Select((x, i) => new PieSeries<double>
 {
 Name = string.IsNullOrWhiteSpace(x.Name) ? "Przychody" : x.Name,
 Values = new[] { (double)Math.Abs(x.Amount) },
 InnerRadius =0,
 DataLabelsPaint = new SolidColorPaint(SKColors.White),
 DataLabelsSize =11,
 DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
 DataLabelsFormatter = point => string.Format(CultureInfo.CurrentCulture, "{0:N2} z³", point.Coordinate.PrimaryValue),
 Fill = new SolidColorPaint(PiePalette[i % PiePalette.Length]),
 Stroke = null
 }).Cast<ISeries>().ToArray();
 }

 IncomeBySourceSeries = incSeries;
 PieIncomeSeries = incSeries;
 }
 catch
 {
 ExpensesByCategorySeries = Array.Empty<ISeries>();
 IncomeBySourceSeries = Array.Empty<ISeries>();
 PieExpenseSeries = Array.Empty<ISeries>();
 PieIncomeSeries = Array.Empty<ISeries>();
 }
 }

 public void LoadTransactions(DateTime start, DateTime end)
 {
 Incomes.Clear();
 Expenses.Clear();
 PlannedTransactions.Clear(); // pozostawione puste – brak implementacji zaplanowanych

 try
 {
 // ===== PRZYCHODY ===== (lokalny odczyt – nie u¿ywa nieistniej¹cego DatabaseService.GetIncomes)
 var dtInc = LoadIncomesRaw(_userId, start, end);
 foreach (var r in ToRows(dtInc))
 {
 var rawSrc = SafeString(r, "Source");
 var item = new TransactionItem
 {
 Id = SafeInt(r, "Id"),
 Date = SafeDate(r, "Date"),
 Category = SafeString(r, "CategoryName"),
 RawSource = rawSrc,
 Account = rawSrc,
 Description = SafeString(r, "Description"),
 Kind = "Przychód",
 Amount = Math.Abs(SafeDecimal(r, "Amount"))
 };
 item.NormalizeAccount();
 Incomes.Add(item);
 }

 // ===== WYDATKI =====
 var dtExp = DatabaseService.GetExpenses(_userId, start, end);
 foreach (var r in ToRows(dtExp))
 {
 var rawSrc = SafeString(r, "Source");
 var item = new TransactionItem
 {
 Id = SafeInt(r, "Id"),
 Date = SafeDate(r, "Date"),
 Category = SafeString(r, "CategoryName"),
 RawSource = rawSrc,
 Account = rawSrc,
 Description = SafeString(r, "Description"),
 Kind = "Wydatek",
 Amount = Math.Abs(SafeDecimal(r, "Amount"))
 };
 item.NormalizeAccount();
 Expenses.Add(item);
 }
 }
 catch { }
 }

 private DataTable LoadIncomesRaw(int userId, DateTime start, DateTime end)
 {
 var dt = new DataTable();
 try
 {
 using var con = DatabaseService.GetConnection();
 using var cmd = con.CreateCommand();
 cmd.CommandText = @"SELECT i.Id, i.Date, i.Amount, i.Description, i.Source, i.CategoryId, c.Name AS CategoryName
 FROM Incomes i
 LEFT JOIN Categories c ON c.Id = i.CategoryId
 WHERE i.UserId = @u AND date(i.Date) >= date(@from) AND date(i.Date) <= date(@to)
 ORDER BY date(i.Date) DESC, i.Id DESC;";
 cmd.Parameters.AddWithValue("@u", userId);
 cmd.Parameters.AddWithValue("@from", start.Date.ToString("yyyy-MM-dd"));
 cmd.Parameters.AddWithValue("@to", end.Date.ToString("yyyy-MM-dd"));
 using var r = cmd.ExecuteReader();
 dt.Load(r);
 }
 catch { }
 return dt;
 }

 public void GenerateInsights(DateTime start, DateTime end)
 {
 Insights.Clear();
 try
 {
 var thisRange = ToRows(DatabaseService.GetExpenses(_userId, start, end));
 var prevRange = ToRows(DatabaseService.GetExpenses(_userId, start.AddMonths(-1), end.AddMonths(-1)));

 decimal sumThis = Sum(thisRange);
 decimal sumPrev = Sum(prevRange);
 if (sumPrev >0m)
 {
 var diffPct = (double)((sumPrev - sumThis) / sumPrev *100m);
 Insights.Add($"W tym okresie wydajesz {diffPct:+0;-0;0}% wzglêdem poprzedniego.");
 }
 else
 {
 Insights.Add("Brak danych do porównania z poprzednim okresem.");
 }

 var top = thisRange
 .Select(r => SafeDecimal(r, "Amount"))
 .DefaultIfEmpty(0m)
 .Max();
 if (top >0m)
 Insights.Add($"Najwiêkszy wydatek w tym okresie to {top.ToString("N2", CultureInfo.CurrentCulture)} z³.");

 var count = thisRange.Count();
 Insights.Add($"Liczba transakcji: {count}.");
 }
 catch
 {
 Insights.Add("Nie uda³o siê wygenerowaæ insightów.");
 }
 }

 public void GenerateAlerts(DateTime start, DateTime end)
 {
 Alerts.Clear();
 try
 {
 var rows = ToRows(DatabaseService.GetExpenses(_userId, start, end));
 var uncategorized = rows.Count(r => string.IsNullOrWhiteSpace(SafeString(r, "CategoryName")));
 if (uncategorized >0)
 Alerts.Add($"{uncategorized} transakcji bez kategorii.");

 // Usuniêto alert zaplanowanych wydatków – brak implementacji zaplanowanych w tej wersji
 if (!Alerts.Any()) Alerts.Add("Brak alertów.");
 }
 catch
 {
 Alerts.Add("Nie uda³o siê wygenerowaæ alertów.");
 }
 }

 public void GenerateForecast(DateTime start, DateTime end)
 {
 try
 {
 var rows = ToRows(DatabaseService.GetExpenses(_userId, start, end));
 var days = Math.Max(1, (end.Date - start.Date).Days +1);
 var spent = Sum(rows);
 var avgPerDay = spent / days;

 var monthEnd = new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month));
 var remainingDays = Math.Max(0, (monthEnd - end.Date).Days);
 var predictedSpending = spent + (avgPerDay * remainingDays);

 var snap = DatabaseService.GetMoneySnapshot(_userId);
 var predictedFreeCash = snap.Cash - predictedSpending;

 Forecast = new ForecastModel
 {
 PredictedSpending = predictedSpending,
 PredictedFreeCash = predictedFreeCash
 };
 }
 catch
 {
 Forecast = new ForecastModel();
 }
 }

 private static System.Collections.Generic.IEnumerable<DataRow> ToRows(DataTable? dt)
 => dt == null ? Enumerable.Empty<DataRow>() : dt.Rows.Cast<DataRow>();

 private static decimal Sum(System.Collections.Generic.IEnumerable<DataRow> rows)
 {
 decimal s =0m; foreach (var r in rows) { try { var a = SafeDecimal(r, "Amount"); s += Math.Abs(a); } catch { } } return s;
 }
 private static string SafeString(DataRow r, string col) => r[col] == null || r[col] == System.DBNull.Value ? string.Empty : r[col]?.ToString() ?? string.Empty;
 private static decimal SafeDecimal(DataRow r, string col) => decimal.TryParse(r[col]?.ToString(), out var v) ? v :0m;
 private static int SafeInt(DataRow r, string col) => int.TryParse(r[col]?.ToString(), out var v) ? v :0;
 private static DateTime SafeDate(DataRow r, string col) => DateTime.TryParse(r[col]?.ToString(), out var v) ? v : DateTime.MinValue;

 public event PropertyChangedEventHandler? PropertyChanged;
 private void OnPropertyChanged([CallerMemberName] string? name = null)
 => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
 }
}
