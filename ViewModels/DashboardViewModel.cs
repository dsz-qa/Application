using Finly.Services.Features;
using Finly.Services.SpecificPages;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;


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
        public string DateDisplay => Date == DateTime.MinValue ? "" : Date.ToString("dd.MM.yyyy");


        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Sformatowane źródło/konto (fallback dla prostych widoków).
        /// </summary>
        public string Account { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// "Przychód" / "Wydatek" / "Transfer"
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        public decimal Amount { get; set; }
        public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";

        /// <summary>
        /// Oryginalny tekst źródła z DB (np. Incomes.Source).
        /// </summary>
        public string RawSource { get; set; } = string.Empty;

        /// <summary>
        /// Dla transferów/planowanych: skąd / dokąd.
        /// Dla wydatku: FromAccount; dla przychodu: ToAccount.
        /// </summary>
        public string FromAccount { get; set; } = string.Empty;
        public string ToAccount { get; set; } = string.Empty;

        public void NormalizeAccount()
        {
            if (string.IsNullOrWhiteSpace(Account) && !string.IsNullOrWhiteSpace(RawSource))
            {
                var s = RawSource.Trim();

                if (s.Equals("Wolna gotówka", StringComparison.OrdinalIgnoreCase)) Account = "Wolna gotówka";
                else if (s.Equals("Odłożona gotówka", StringComparison.OrdinalIgnoreCase)) Account = "Odłożona gotówka";
                else if (s.StartsWith("Konto", StringComparison.OrdinalIgnoreCase)) Account = s;   // np. "Konto: mBank"
                else if (s.StartsWith("Koperta", StringComparison.OrdinalIgnoreCase)) Account = s; // np. "Koperta: Jedzenie"
                else Account = s;
            }
        }
    }

    public sealed class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly int _userId;

        public ObservableCollection<string> Insights { get; } = new();
        public ObservableCollection<string> Alerts { get; } = new();

        private ForecastModel _forecast = new ForecastModel();
        public ForecastModel Forecast
        {
            get => _forecast;
            private set { _forecast = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TransactionItem> Incomes { get; } = new();
        public ObservableCollection<TransactionItem> Expenses { get; } = new();
        public ObservableCollection<TransactionItem> PlannedTransactions { get; } = new();

        // Legacy properties kept for backward compatibility
        private ISeries[] _pieExpenseSeries = Array.Empty<ISeries>();
        public ISeries[] PieExpenseSeries
        {
            get => _pieExpenseSeries;
            private set { _pieExpenseSeries = value; OnPropertyChanged(); }
        }

        private ISeries[] _pieIncomeSeries = Array.Empty<ISeries>();
        public ISeries[] PieIncomeSeries
        {
            get => _pieIncomeSeries;
            private set { _pieIncomeSeries = value; OnPropertyChanged(); }
        }

        // New properties bound by XAML
        private ISeries[] _expensesByCategorySeries = Array.Empty<ISeries>();
        public ISeries[] ExpensesByCategorySeries
        {
            get => _expensesByCategorySeries;
            private set { _expensesByCategorySeries = value; OnPropertyChanged(); }
        }

        private ISeries[] _incomeBySourceSeries = Array.Empty<ISeries>();
        public ISeries[] IncomeBySourceSeries
        {
            get => _incomeBySourceSeries;
            private set { _incomeBySourceSeries = value; OnPropertyChanged(); }
        }

        // ===== RangeStats (kafelek "Do ogarnięcia") =====
        public sealed class RangeStatsVm : INotifyPropertyChanged
        {
            private int _total;
            private int _missingCategory;
            private int _planned;
            private string _message = "Brak danych w tym okresie.";
            private string _actionText = "Przejdź do transakcji";
            private bool _hasAction;

            // NEW: route dla ShellWindow.NavigateTo(...)
            private string _actionRoute = "transactions";
            public string ActionRoute { get => _actionRoute; set { _actionRoute = value; OnPropertyChanged(); } }

            public int Total { get => _total; set { _total = value; OnPropertyChanged(); } }
            public int MissingCategory { get => _missingCategory; set { _missingCategory = value; OnPropertyChanged(); } }
            public int Planned { get => _planned; set { _planned = value; OnPropertyChanged(); } }

            public string Message { get => _message; set { _message = value; OnPropertyChanged(); } }
            public string ActionText { get => _actionText; set { _actionText = value; OnPropertyChanged(); } }
            public bool HasAction { get => _hasAction; set { _hasAction = value; OnPropertyChanged(); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }



        public RangeStatsVm RangeStats { get; } = new();

        public DashboardViewModel(int userId)
        {
            _userId = userId;
        }

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
            SKColor.Parse("#9AA0A6")  // gray
        };

        public void RefreshCharts(DateTime start, DateTime end)
        {
            try
            {
                // ===== EXPENSE PIE =====
                var expenseGroups = Expenses
                    .Where(t => t.Kind == "Wydatek" && t.Amount > 0)
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "(brak kategorii)" : t.Category)
                    .OrderByDescending(g => g.Sum(x => x.Amount))
                    .ToList();

                var expSeries = expenseGroups.Select((g, i) => new PieSeries<double>
                {
                    Name = g.Key,
                    Values = new[] { (double)g.Sum(x => x.Amount) },
                    InnerRadius = 0,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsFormatter = p => string.Format(CultureInfo.CurrentCulture, "{0:N2} zł", p.Coordinate.PrimaryValue),
                    Fill = new SolidColorPaint(PiePalette[i % PiePalette.Length]),
                    Stroke = null
                }).Cast<ISeries>().ToArray();

                if (expSeries.Length == 0)
                {
                    var exp = DatabaseService.GetSpendingByCategorySafe(_userId, start, end)
                              ?? new System.Collections.Generic.List<DatabaseService.CategoryAmountDto>();

                    expSeries = exp.Select((x, i) => new PieSeries<double>
                    {
                        Name = string.IsNullOrWhiteSpace(x.Name) ? "(brak kategorii)" : x.Name,
                        Values = new[] { (double)Math.Abs(x.Amount) },
                        InnerRadius = 0,
                        DataLabelsPaint = new SolidColorPaint(SKColors.White),
                        DataLabelsSize = 11,
                        DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                        DataLabelsFormatter = p => string.Format(CultureInfo.CurrentCulture, "{0:N2} zł", p.Coordinate.PrimaryValue),
                        Fill = new SolidColorPaint(PiePalette[i % PiePalette.Length]),
                        Stroke = null
                    }).Cast<ISeries>().ToArray();
                }

                ExpensesByCategorySeries = expSeries;
                PieExpenseSeries = expSeries;

                // ===== INCOME PIE =====
                var incomeGroups = Incomes
                    .Where(t => t.Kind == "Przychód" && t.Amount > 0)
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Przychody" : t.Category)
                    .OrderByDescending(g => g.Sum(x => x.Amount))
                    .ToList();

                var incSeries = incomeGroups.Select((g, i) => new PieSeries<double>
                {
                    Name = g.Key,
                    Values = new[] { (double)g.Sum(x => x.Amount) },
                    InnerRadius = 0,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsFormatter = p => string.Format(CultureInfo.CurrentCulture, "{0:N2} zł", p.Coordinate.PrimaryValue),
                    Fill = new SolidColorPaint(PiePalette[i % PiePalette.Length]),
                    Stroke = null
                }).Cast<ISeries>().ToArray();

                if (incSeries.Length == 0)
                {
                    var inc = DatabaseService.GetIncomeByCategorySafe(_userId, start, end)
                              ?? new List<DatabaseService.CategoryAmountDto>();


                    incSeries = inc.Select((x, i) => new PieSeries<double>
                    {
                        Name = string.IsNullOrWhiteSpace(x.Name) ? "Przychody" : x.Name,
                        Values = new[] { (double)Math.Abs(x.Amount) },
                        InnerRadius = 0,
                        DataLabelsPaint = new SolidColorPaint(SKColors.White),
                        DataLabelsSize = 11,
                        DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                        DataLabelsFormatter = p => string.Format(CultureInfo.CurrentCulture, "{0:N2} zł", p.Coordinate.PrimaryValue),
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
            PlannedTransactions.Clear();

            try
            {
                // ===== PRZYCHODY =====
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
                        Amount = Math.Abs(SafeDecimal(r, "Amount")),
                        FromAccount = "",
                        ToAccount = rawSrc
                    };

                    item.NormalizeAccount();
                    item.ToAccount = item.Account;

                    Incomes.Add(item);
                }

                // ===== WYDATKI =====
                var dtExp = LoadExpensesRaw(_userId, start, end);
                foreach (var r in ToRows(dtExp))
                {
                    var accountText = SafeInt(r, "AccountId") > 0 ? "Konto bankowe" : "Gotówka";

                    var item = new TransactionItem
                    {
                        Id = SafeInt(r, "Id"),
                        Date = SafeDate(r, "Date"),
                        Category = SafeString(r, "CategoryName"),
                        RawSource = accountText,
                        Account = accountText,
                        Description = SafeString(r, "Description"),
                        Kind = "Wydatek",
                        Amount = Math.Abs(SafeDecimal(r, "Amount")),
                        FromAccount = accountText,
                        ToAccount = ""
                    };

                    item.NormalizeAccount();
                    item.FromAccount = item.Account;

                    Expenses.Add(item);
                }

                // ===== ZAPLANOWANE =====
                LoadPlannedTransactions(start, end);

                // ===== KAFEL "Do ogarnięcia" =====
                UpdateRangeStats();
            }
            catch
            {
                UpdateRangeStats(); // nawet jak błąd, ustaw sensowny stan UI
            }
        }

        private void UpdateRangeStats()
        {
            try
            {
                var total = Incomes.Count + Expenses.Count;
                var planned = PlannedTransactions.Count;

                static bool IsMissingCategory(string? s)
                    => string.IsNullOrWhiteSpace(s) || s.Trim().Equals("-", StringComparison.OrdinalIgnoreCase);

                var missingCategory =
                    Incomes.Count(x => IsMissingCategory(x.Category)) +
                    Expenses.Count(x => IsMissingCategory(x.Category));

                RangeStats.Total = total;
                RangeStats.Planned = planned;
                RangeStats.MissingCategory = missingCategory;

                if (total == 0 && planned == 0)
                {
                    RangeStats.Message = "W tym okresie nie ma danych. Zmień zakres albo dodaj pierwszą transakcję.";
                    RangeStats.ActionText = "Dodaj transakcję";
                    RangeStats.ActionRoute = "addexpense";   // ✅ AddExpensePage
                    RangeStats.HasAction = true;
                    return;
                }

                if (missingCategory > 0)
                {
                    RangeStats.Message = $"Masz {missingCategory} transakcji bez kategorii. Uzupełnij je, żeby raporty były dokładniejsze.";
                    RangeStats.ActionText = "Przejdź do transakcji";
                    RangeStats.ActionRoute = "transactions"; // ✅ TransactionsPage
                    RangeStats.HasAction = true;
                    return;
                }

                if (planned > 0)
                {
                    RangeStats.Message = $"Masz {planned} zaplanowanych transakcji. Sprawdź, czy wszystko jest aktualne.";
                    RangeStats.ActionText = "Zobacz zaplanowane";
                    RangeStats.ActionRoute = "transactions"; // ✅ najprościej: planned są w transakcjach
                    RangeStats.HasAction = true;
                    return;
                }

                RangeStats.Message = "Dane wyglądają dobrze. Możesz przejść do raportów lub dodać nowe transakcje.";
                RangeStats.ActionText = "Otwórz raporty";
                RangeStats.ActionRoute = "reports";         // ✅ ReportsPage
                RangeStats.HasAction = true;
            }
            catch
            {
                RangeStats.Total = 0;
                RangeStats.Planned = 0;
                RangeStats.MissingCategory = 0;
                RangeStats.Message = "Nie udało się podsumować danych dla okresu.";
                RangeStats.ActionText = "Odśwież";
                RangeStats.ActionRoute = "dashboard";       // fallback
                RangeStats.HasAction = false;
            }
        }


        private void LoadPlannedTransactions(DateTime start, DateTime end)
        {
            try
            {
                var plannedIncome = new System.Collections.Generic.List<TransactionItem>();
                var plannedExpense = new System.Collections.Generic.List<TransactionItem>();

                // ===== PRZYCHODY ZAPLANOWANE =====
                using (var con = DatabaseService.GetConnection())
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT i.Id, i.Date, i.Amount, i.Description, i.Source, i.CategoryId, c.Name AS CategoryName
FROM Incomes i
LEFT JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId=@u
  AND i.IsPlanned=1
  AND date(i.Date) >= date(@from)
  AND date(i.Date) <= date(@to)
ORDER BY date(i.Date) DESC, i.Id DESC;";

                    cmd.Parameters.AddWithValue("@u", _userId);
                    cmd.Parameters.AddWithValue("@from", start.Date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@to", end.Date.ToString("yyyy-MM-dd"));

                    using var r = cmd.ExecuteReader();
                    var dt = new DataTable();
                    dt.Load(r);

                    foreach (DataRow row in dt.Rows)
                    {
                        var rawSource = SafeString(row, "Source");

                        var item = new TransactionItem
                        {
                            Id = SafeInt(row, "Id"),
                            Date = SafeDate(row, "Date"),
                            Category = SafeString(row, "CategoryName"),
                            RawSource = rawSource,
                            Account = rawSource,
                            Description = SafeString(row, "Description"),
                            Kind = "Przychód",
                            Amount = Math.Abs(SafeDecimal(row, "Amount")),
                            FromAccount = "",
                            ToAccount = rawSource
                        };

                        item.NormalizeAccount();
                        item.ToAccount = item.Account;

                        plannedIncome.Add(item);
                    }
                }

                // ===== WYDATKI ZAPLANOWANE =====
                using (var con = DatabaseService.GetConnection())
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT e.Id, e.Date, e.Amount, e.Description, e.CategoryId, e.AccountId, c.Name AS CategoryName
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId=@u
  AND e.IsPlanned=1
  AND date(e.Date) >= date(@from)
  AND date(e.Date) <= date(@to)
ORDER BY date(e.Date) DESC, e.Id DESC;";

                    cmd.Parameters.AddWithValue("@u", _userId);
                    cmd.Parameters.AddWithValue("@from", start.Date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@to", end.Date.ToString("yyyy-MM-dd"));

                    using var r = cmd.ExecuteReader();
                    var dt = new DataTable();
                    dt.Load(r);

                    foreach (DataRow row in dt.Rows)
                    {
                        var accountText = SafeInt(row, "AccountId") > 0 ? "Konto bankowe" : "Gotówka";

                        var item = new TransactionItem
                        {
                            Id = SafeInt(row, "Id"),
                            Date = SafeDate(row, "Date"),
                            Category = SafeString(row, "CategoryName"),
                            RawSource = accountText,
                            Account = accountText,
                            Description = SafeString(row, "Description"),
                            Kind = "Wydatek",
                            Amount = Math.Abs(SafeDecimal(row, "Amount")),
                            FromAccount = accountText,
                            ToAccount = ""
                        };

                        item.NormalizeAccount();
                        item.FromAccount = item.Account;

                        plannedExpense.Add(item);
                    }
                }

                // ===== WYKRYCIE TRANSFERÓW (heurystyka) =====
                static bool IsTransferLike(string s)
                    => !string.IsNullOrWhiteSpace(s)
                       && (s.StartsWith("Przelew", StringComparison.OrdinalIgnoreCase)
                           || s.StartsWith("Transfer", StringComparison.OrdinalIgnoreCase));

                var usedIncomeIds = new System.Collections.Generic.HashSet<int>();
                var usedExpenseIds = new System.Collections.Generic.HashSet<int>();

                foreach (var inc in plannedIncome.Where(i => IsTransferLike(i.RawSource) || IsTransferLike(i.Account)))
                {
                    if (usedIncomeIds.Contains(inc.Id)) continue;

                    var match = plannedExpense.FirstOrDefault(e =>
                        !usedExpenseIds.Contains(e.Id) &&
                        e.Date.Date == inc.Date.Date &&
                        Math.Round(e.Amount, 2) == Math.Round(inc.Amount, 2));

                    if (match == null) continue;

                    usedIncomeIds.Add(inc.Id);
                    usedExpenseIds.Add(match.Id);

                    PlannedTransactions.Add(new TransactionItem
                    {
                        Id = inc.Id,
                        Date = inc.Date,
                        Category = !string.IsNullOrWhiteSpace(match.Category) ? match.Category : inc.Category,
                        Kind = "Transfer",
                        Amount = inc.Amount,
                        Description = string.IsNullOrWhiteSpace(inc.Description) ? match.Description : inc.Description,
                        FromAccount = match.Account,
                        ToAccount = inc.Account,
                        Account = "",
                        RawSource = "Transfer"
                    });
                }

                // ===== DODAJ RESZTĘ =====
                foreach (var inc in plannedIncome.Where(i => !usedIncomeIds.Contains(i.Id)))
                    PlannedTransactions.Add(inc);

                foreach (var exp in plannedExpense.Where(e => !usedExpenseIds.Contains(e.Id)))
                    PlannedTransactions.Add(exp);
            }
            catch
            {
                // celowo cicho – jak wcześniej
            }
        }

        private DataTable LoadExpensesRaw(int userId, DateTime start, DateTime end)
        {
            var dt = new DataTable();
            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();

                cmd.CommandText = @"
SELECT e.Id, e.Date, e.Amount, e.Description, e.CategoryId, e.AccountId, c.Name AS CategoryName
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId = @u
  AND IFNULL(e.IsPlanned,0)=0
  AND date(e.Date) >= date(@from)
  AND date(e.Date) <= date(@to)
ORDER BY date(e.Date) DESC, e.Id DESC;";


                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@from", start.Date.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", end.Date.ToString("yyyy-MM-dd"));

                using var r = cmd.ExecuteReader();
                dt.Load(r);
            }
            catch { }
            return dt;
        }

        private DataTable LoadIncomesRaw(int userId, DateTime start, DateTime end)
        {
            var dt = new DataTable();
            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();

                cmd.CommandText = @"
SELECT i.Id, i.Date, i.Amount, i.Description, i.Source, i.CategoryId, c.Name AS CategoryName
FROM Incomes i
LEFT JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId = @u
  AND IFNULL(i.IsPlanned,0)=0
  AND date(i.Date) >= date(@from)
  AND date(i.Date) <= date(@to)
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

        public void GenerateInsights(DateTime start, DateTime end, DateTime prevStart, DateTime prevEnd)
        {
            Insights.Clear();

            try
            {
                // wydatki: ten okres vs poprzedni porównywalny
                var thisExp = ToRows(LoadExpensesRaw(_userId, start, end));
                var prevExp = ToRows(LoadExpensesRaw(_userId, prevStart, prevEnd));


                decimal sumThis = Sum(thisExp);
                decimal sumPrev = Sum(prevExp);

                if (sumPrev > 0m)
                {
                    var diffPct = (double)((sumThis - sumPrev) / sumPrev * 100m);
                    Insights.Add($"W tym okresie wydajesz {diffPct:+0;-0;0}% względem poprzedniego.");
                }
                else
                {
                    if (sumThis > 0m)
                        Insights.Add("W tym okresie wydajesz więcej niż w poprzednim (w poprzednim brak wydatków).");
                    else
                        Insights.Add("Brak danych do porównania z poprzednim okresem.");
                }


                // największy wydatek: kwota + kategoria + opis
                DataRow? topRow = null;
                decimal topAmt = 0m;

                foreach (var r in thisExp)
                {
                    var a = Math.Abs(SafeDecimal(r, "Amount"));
                    if (a > topAmt)
                    {
                        topAmt = a;
                        topRow = r;
                    }
                }

                if (topRow != null && topAmt > 0m)
                {
                    var cat = SafeString(topRow, "CategoryName");
                    if (string.IsNullOrWhiteSpace(cat)) cat = "(brak kategorii)";

                    var desc = SafeString(topRow, "Description");
                    if (string.IsNullOrWhiteSpace(desc)) desc = "(brak opisu)";

                    // lekkie zabezpieczenie długości, żeby UI nie robił ściany tekstu
                    if (desc.Length > 80) desc = desc.Substring(0, 80) + "…";

                    Insights.Add($"Największy wydatek w tym okresie to {topAmt:N2} zł — {cat} — {desc}.");
                }

                // liczba transakcji = przychody + wydatki (zakres z periodbara)
                var expRaw = ToRows(LoadExpensesRaw(_userId, start, end));
                var incRaw = ToRows(LoadIncomesRaw(_userId, start, end));

                int count = expRaw.Count() + incRaw.Count();
                Insights.Add($"Liczba transakcji: {count}.");
            }
            catch
            {
                Insights.Add("Nie udało się wygenerować insightów.");
            }
        }



        public void GenerateAlerts(DateTime start, DateTime end)
        {
            Alerts.Clear();

            try
            {
                // 1) Alerty przekroczenia budżetów
                var over = BudgetService.GetOverBudgetAlerts(_userId, start, end)
                                        .OrderByDescending(x => x.OverAmount)
                                        .ToList();

                foreach (var b in over.Take(5))
                {
                    Alerts.Add($"Przekroczono budżet „{b.Name}” o {b.OverAmount:N2} zł (okres: {b.Period}).");
                }

                if (over.Count > 5)
                    Alerts.Add($"+{over.Count - 5} kolejnych przekroczonych budżetów.");

                // 2) Jeśli chcesz zostawić też „bez kategorii” – zostaw.
                // Jeśli NIE chcesz, to usuń cały blok poniżej.
                /*
                var exp = ToRows(LoadExpensesRaw(_userId, start, end));
                var inc = ToRows(LoadIncomesRaw(_userId, start, end));
                int uncategorized =
                    exp.Count(r => string.IsNullOrWhiteSpace(SafeString(r, "CategoryName"))) +
                    inc.Count(r => string.IsNullOrWhiteSpace(SafeString(r, "CategoryName")));
                if (uncategorized > 0)
                    Alerts.Add($"{uncategorized} transakcji bez kategorii.");
                */

                if (!Alerts.Any())
                    Alerts.Add("Brak alertów budżetowych w tym okresie.");
            }
            catch
            {
                Alerts.Add("Nie udało się wygenerować alertów budżetowych.");
            }
        }


        public void GenerateForecast(DateTime start, DateTime end)
        {
            try
            {
                var rows = ToRows(LoadExpensesRaw(_userId, start, end));
                var days = Math.Max(1, (end.Date - start.Date).Days + 1);
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
            decimal s = 0m;
            foreach (var r in rows)
            {
                try
                {
                    var a = SafeDecimal(r, "Amount");
                    s += Math.Abs(a);
                }
                catch { }
            }
            return s;
        }

        private static string SafeString(DataRow r, string col)
            => r[col] == null || r[col] == DBNull.Value ? string.Empty : r[col]?.ToString() ?? string.Empty;

        private static decimal SafeDecimal(DataRow r, string col)
            => decimal.TryParse(r[col]?.ToString(), out var v) ? v : 0m;

        private static int SafeInt(DataRow r, string col)
            => int.TryParse(r[col]?.ToString(), out var v) ? v : 0;

        private static DateTime SafeDate(DataRow r, string col)
            => DateTime.TryParse(r[col]?.ToString(), out var v) ? v : DateTime.MinValue;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
