using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Finly.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Microsoft.Win32;
using SkiaSharp; // Dodaj ten import na górze pliku

namespace Finly.ViewModels
{
    public class ChartsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public enum Period { Today, Week, Month, Year, All }
        public enum Mode { Expenses, Incomes, Transfer, Cashflow }

        private Period _selectedPeriod = Period.Month;
        public Period SelectedPeriod
        {
            get => _selectedPeriod;
            set
            {
                if (_selectedPeriod != value)
                {
                    _selectedPeriod = value;
                    Raise(nameof(SelectedPeriod));
                    _useCustomRange = false;
                    LoadStatistics();
                }
            }
        }

        private Mode _selectedMode = Mode.Expenses;
        public Mode SelectedMode
        {
            get => _selectedMode;
            set
            {
                if (_selectedMode != value)
                {
                    _selectedMode = value;
                    Raise(nameof(SelectedMode));
                    LoadStatistics();
                }
            }
        }

        // LiveCharts series
        public ObservableCollection<ISeries> CategoriesSeries { get; } = new();
        public ObservableCollection<ISeries> TrendSeries { get; } = new();
        public ObservableCollection<ISeries> AccountsSeries { get; } = new();
        public ObservableCollection<ISeries> WeekdaySeries { get; } = new();

        // Axes (bind to XAxes in XAML)
        public ObservableCollection<Axis> TrendXAxes { get; } = new();
        public ObservableCollection<Axis> AccountsXAxes { get; } = new();
        public ObservableCollection<Axis> WeekdayXAxes { get; } = new();

        // Labels (used to populate axes)
        public ObservableCollection<string> TrendLabels { get; } = new();
        public ObservableCollection<string> AccountLabels { get; } = new();
        public ObservableCollection<string> WeekdayLabels { get; } = new();

        private readonly int _userId;

        public ChartsViewModel()
        {
            _userId = UserService.GetCurrentUserId();
            try
            {
                DatabaseService.DataChanged += (_, __) => LoadStatistics();
            }
            catch
            {
                // jak coœ nie ma eventu, po prostu ignorujemy
            }

            LoadStatistics();
        }

        // ===== Ustawianie trybu/okresu z UI =====

        public void SetPeriod(string period)
        {
            if (Enum.TryParse(period, true, out Period p))
                SelectedPeriod = p;
        }

        public void SetMode(string mode)
        {
            if (Enum.TryParse(mode, true, out Mode m))
                SelectedMode = m;
        }

        public void SetCustomRange(DateTime start, DateTime end)
        {
            _customFrom = start.Date;
            _customTo = end.Date;
            _useCustomRange = true;
            LoadStatistics();
        }

        private bool _useCustomRange;
        private DateTime? _customFrom;
        private DateTime? _customTo;

        private (DateTime? From, DateTime? To, string Bucket) ResolveRange()
        {
            if (_useCustomRange && _customFrom.HasValue && _customTo.HasValue)
            {
                var from = _customFrom.Value.Date;
                var to = _customTo.Value.Date;
                var span = (to - from).TotalDays;
                string bucket = span <= 7 ? "day" : span <= 35 ? "week" : "month";
                return (from, to, bucket);
            }

            var today = DateTime.Today;

            return SelectedPeriod switch
            {
                Period.Today => (today, today, "day"),
                Period.Week => (today.AddDays(-6), today, "day"),
                Period.Month => (new DateTime(today.Year, today.Month, 1),
                                 new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1),
                                 "week"),
                Period.Year => (new DateTime(today.Year, 1, 1),
                                new DateTime(today.Year, 12, 31),
                                "month"),
                _ => (null, null, "month") // All
            };
        }

        // ===== Publiczne API: odœwie¿enie statystyk =====

        public void LoadStatistics()
        {
            CategoriesSeries.Clear();
            TrendSeries.Clear();
            AccountsSeries.Clear();
            WeekdaySeries.Clear();

            TrendLabels.Clear();
            AccountLabels.Clear();
            WeekdayLabels.Clear();

            TrendXAxes.Clear();
            AccountsXAxes.Clear();
            WeekdayXAxes.Clear();

            var (from, to, bucket) = ResolveRange();

            try
            {
                var data = BuildAggregates(from, to, bucket);

                // --- wykres ko³owy (kategorie) ---
                if (data.ByCategory.Count == 0)
                {
                    CategoriesSeries.Add(new PieSeries<double>
                    {
                        Name = "Brak danych",
                        Values = new[] { 1d }
                    });
                }
                else
                {
                    foreach (var cat in data.ByCategory)
                    {
                        CategoriesSeries.Add(new PieSeries<double>
                        {
                            Name = cat.Name,
                            Values = new[] { (double)cat.Sum }
                        });
                    }
                }

                // --- trend w czasie ---
                if (data.Trend.Count == 0)
                {
                    TrendLabels.Add("-");
                    TrendSeries.Add(new LineSeries<double>
                    {
                        Values = new[] { 0d },
                        GeometrySize = 8,
                        Fill = null
                    });
                }
                else
                {
                    foreach (var t in data.Trend)
                        TrendLabels.Add(t.Label);

                    TrendSeries.Add(new LineSeries<double>
                    {
                        Values = data.Trend.Select(t => (double)t.Value).ToArray(),
                        GeometrySize = 8,
                        Fill = null
                    });
                }

                TrendXAxes.Add(new Axis
                {
                    Labels = TrendLabels.ToArray(),
                    LabelsRotation = 45,
                    Name = "Okres",
                    LabelsPaint = new SolidColorPaint(SKColors.Gray)
                });

                // --- konta / Ÿród³a ---
                if (data.ByAccount.Count == 0)
                {
                    AccountLabels.Add("-");
                    AccountsSeries.Add(new ColumnSeries<double>
                    {
                        Values = new[] { 0d }
                    });
                }
                else
                {
                    foreach (var a in data.ByAccount)
                        AccountLabels.Add(a.Name);

                    AccountsSeries.Add(new ColumnSeries<double>
                    {
                        Values = data.ByAccount.Select(a => (double)a.Sum).ToArray()
                    });
                }

                AccountsXAxes.Add(new Axis
                {
                    Labels = AccountLabels.ToArray(),
                    Name = "Konto / Ÿród³o",
                    LabelsPaint = new SolidColorPaint(SKColors.Gray)
                });

                // --- dni tygodnia ---
                if (data.ByWeekday.Count == 0)
                {
                    WeekdayLabels.Add("-");
                    WeekdaySeries.Add(new ColumnSeries<double>
                    {
                        Values = new[] { 0d }
                    });
                }
                else
                {
                    foreach (var d in data.ByWeekday)
                        WeekdayLabels.Add(d.Name);

                    WeekdaySeries.Add(new ColumnSeries<double>
                    {
                        Values = data.ByWeekday.Select(d => (double)d.Sum).ToArray()
                    });
                }

                WeekdayXAxes.Add(new Axis
                {
                    Labels = WeekdayLabels.ToArray(),
                    Name = "Dzieñ tygodnia",
                    LabelsPaint = new SolidColorPaint(SKColors.Gray)
                });
            }
            catch
            {
                // fallback: jedna szara seria "brak danych"
                CategoriesSeries.Add(new PieSeries<double> { Name = "Brak danych", Values = new[] { 1d } });

                TrendLabels.Add("-");
                TrendSeries.Add(new LineSeries<double> { Values = new[] { 0d }, Fill = null });
                TrendXAxes.Add(new Axis { Labels = TrendLabels.ToArray() });

                AccountLabels.Add("-");
                AccountsSeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                AccountsXAxes.Add(new Axis { Labels = AccountLabels.ToArray() });

                WeekdayLabels.Add("-");
                WeekdaySeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                WeekdayXAxes.Add(new Axis { Labels = WeekdayLabels.ToArray() });
            }
        }

        // ===== Eksport PDF / CSV =====

        public async Task ExportToPdfAsync()
        {
            try
            {
                var (from, to, bucket) = ResolveRange();
                var data = BuildAggregates(from, to, bucket);

                var sfd = new SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = $"finly-statystyki-{DateTime.Now:yyyyMMdd-HHmm}.pdf"
                };
                if (sfd.ShowDialog() != true) return;

                QuestPDF.Settings.License = LicenseType.Community;

                var periodText = DescribePeriod(from, to);
                var modeText = SelectedMode switch
                {
                    Mode.Incomes => "Przychody",
                    Mode.Cashflow => "Cashflow",
                    Mode.Transfer => "Transfery",
                    _ => "Wydatki"
                };

                Document.Create(doc =>
                {
                    doc.Page(page =>
                    {
                        page.Margin(30);

                        page.Header().Row(r =>
                        {
                            r.RelativeItem().Text("Finly – Statystyki").SemiBold().FontSize(18);
                            r.ConstantItem(220).AlignRight().Text($"{modeText} – {periodText}");
                        });

                        page.Content().Column(col =>
                        {
                            col.Item().Text($"Suma: {data.SummaryTotal:N2} PLN");

                            if (SelectedMode == Mode.Cashflow)
                            {
                                col.Item().Text(
                                    $"Przychody: {data.TotalIncomes:N2} PLN, " +
                                    $"Wydatki: {data.TotalExpenses:N2} PLN");
                            }

                            // Kategorie
                            col.Item().PaddingTop(10).Text("Podzia³ wg kategorii").Bold();
                            col.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(260);
                                    c.RelativeColumn();
                                });

                                t.Header(h =>
                                {
                                    h.Cell().Text("Kategoria").Bold();
                                    h.Cell().Text("Suma [PLN]").Bold();
                                });

                                foreach (var r2 in data.ByCategory)
                                {
                                    t.Cell().Text(r2.Name);
                                    t.Cell().Text(r2.Sum.ToString("N2"));
                                }
                            });

                            // Trend
                            col.Item().PaddingTop(10).Text("Trend").Bold();
                            col.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(160);
                                    c.RelativeColumn();
                                });

                                t.Header(h =>
                                {
                                    h.Cell().Text("Okres").Bold();
                                    h.Cell().Text("Suma [PLN]").Bold();
                                });

                                foreach (var r2 in data.Trend)
                                {
                                    t.Cell().Text(r2.Label);
                                    t.Cell().Text(r2.Value.ToString("N2"));
                                }
                            });

                            // Konta / Ÿród³a
                            col.Item().PaddingTop(10)
                               .Text(SelectedMode == Mode.Incomes ? "ród³a" : "Konta / koperty")
                               .Bold();

                            col.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(260);
                                    c.RelativeColumn();
                                });

                                t.Header(h =>
                                {
                                    h.Cell().Text("Nazwa").Bold();
                                    h.Cell().Text("Suma [PLN]").Bold();
                                });

                                foreach (var r2 in data.ByAccount)
                                {
                                    t.Cell().Text(r2.Name);
                                    t.Cell().Text(r2.Sum.ToString("N2"));
                                }
                            });

                            // Dni tygodnia
                            col.Item().PaddingTop(10).Text("Dni tygodnia").Bold();
                            col.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(120);
                                    c.RelativeColumn();
                                });

                                t.Header(h =>
                                {
                                    h.Cell().Text("Dzieñ").Bold();
                                    h.Cell().Text("Suma [PLN]").Bold();
                                });

                                foreach (var r2 in data.ByWeekday)
                                {
                                    t.Cell().Text(r2.Name);
                                    t.Cell().Text(r2.Sum.ToString("N2"));
                                }
                            });
                        });

                        page.Footer().AlignRight().Text($"Wygenerowano: {DateTime.Now:yyyy-MM-dd HH:mm}");
                    });
                }).GeneratePdf(sfd.FileName);

                ToastService.Success($"Zapisano PDF: {sfd.FileName}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"B³¹d eksportu PDF: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        public void ExportToCsv()
        {
            try
            {
                var (from, to, bucket) = ResolveRange();
                var data = BuildAggregates(from, to, bucket);

                var sfd = new SaveFileDialog
                {
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = $"finly-statystyki-{DateTime.Now:yyyyMMdd-HHmm}.csv"
                };
                if (sfd.ShowDialog() != true) return;

                using var w = new StreamWriter(sfd.FileName);

                w.WriteLine($"Finly;Tryb;{SelectedMode};Okres;{DescribePeriod(from, to)}");

                w.WriteLine();
                w.WriteLine("Podzia³ wg kategorii");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByCategory)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Trend");
                w.WriteLine("Okres;Suma");
                foreach (var r in data.Trend)
                    w.WriteLine($"{Escape(r.Label)};{r.Value.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                var hdr = SelectedMode == Mode.Incomes ? "ród³a" : "Konta / koperty";
                w.WriteLine(hdr);
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByAccount)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Dni tygodnia");
                w.WriteLine("Dzieñ;Suma");
                foreach (var r in data.ByWeekday)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                ToastService.Success($"Zapisano CSV: {sfd.FileName}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"B³¹d eksportu CSV: {ex.Message}");
            }
        }

        private static string Escape(string? s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace(";", ",");

        private string DescribePeriod(DateTime? from, DateTime? to)
        {
            if (from == null && to == null) return "Ca³y czas";
            if (from == to) return from?.ToString("yyyy-MM-dd") ?? "";
            return $"{from:yyyy-MM-dd} – {to:yyyy-MM-dd}";
        }

        // ===== AGREGACJE (wspólne dla wykresów i eksportu) =====

        private (decimal SummaryTotal, decimal TotalIncomes, decimal TotalExpenses,
                 List<(string Name, decimal Sum)> ByCategory,
                 List<(string Label, decimal Value)> Trend,
                 List<(string Name, decimal Sum)> ByAccount,
                 List<(string Name, decimal Sum)> ByWeekday)
            BuildAggregates(DateTime? from, DateTime? to, string bucket)
        {
            if (SelectedMode == Mode.Cashflow)
            {
                // osobno wydatki i przychody
                var exp = DatabaseService.GetExpenses(_userId, from, to).AsEnumerable()
                    .Select(r => new
                    {
                        Date = SafeDate(r["Date"]),
                        Amount = SafeDecimal(r["Amount"])
                    }).ToList();

                var inc = DatabaseService.GetIncomes(_userId, from, to).AsEnumerable()
                    .Select(r => new
                    {
                        Date = SafeDate(r["Date"]),
                        Amount = SafeDecimal(r["Amount"])
                    }).ToList();

                var incomeTotal = inc.Sum(x => Math.Abs(x.Amount));
                var expenseTotal = exp.Sum(x => Math.Abs(x.Amount));

                var byCategory = new List<(string, decimal)>
                {
                    ("Przychody", incomeTotal),
                    ("Wydatki", expenseTotal)
                };

                var merged = new List<(DateTime Date, decimal Amount)>();
                merged.AddRange(inc.Select(x => (x.Date, Math.Abs(x.Amount))));
                merged.AddRange(exp.Select(x => (x.Date, -Math.Abs(x.Amount))));

                var trend = BuildTrendList(merged, from, to, bucket, null);

                var byAccount = new List<(string, decimal)>
                {
                    ("Przychody", incomeTotal),
                    ("Wydatki", expenseTotal)
                };

                var weekdays = GroupByWeekday(merged, null);
                var order = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = order
                    .Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                var summary = incomeTotal - expenseTotal;
                return (summary, incomeTotal, expenseTotal, byCategory, trend, byAccount, byWeekday);
            }

            if (SelectedMode == Mode.Incomes)
            {
                var dt = DatabaseService.GetIncomes(_userId, from, to);
                var rows = dt.AsEnumerable()
                    .Select(r => new
                    {
                        Date = SafeDate(r["Date"]),
                        Amount = SafeDecimal(r["Amount"]),
                        Category = (r.Table.Columns.Contains("CategoryName")
                            ? (r["CategoryName"]?.ToString() ?? "(brak)")
                            : "(brak)").Trim(),
                        Source = (r.Table.Columns.Contains("Source")
                            ? (r["Source"]?.ToString() ?? "Przychody")
                            : "Przychody").Trim()
                    })
                    .ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));

                var byCategory = rows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
                    .ToList();

                var trend = BuildTrendList(rows.Select(r => (r.Date, r.Amount)), from, to, bucket, false);

                var byAccount = rows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Source) ? "Przychody" : x.Source)
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
                    .ToList();

                var weekdays = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), false);
                var order = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = order
                    .Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                return (total, total, 0m, byCategory, trend, byAccount, byWeekday);
            }

            if (SelectedMode == Mode.Transfer)
            {
                // proste podejœcie: przychody ze Ÿród³em "Przelew" / "Konto: X"
                var dt = DatabaseService.GetIncomes(_userId, from, to);
                var rows = dt.AsEnumerable()
                    .Select(r => new
                    {
                        Date = SafeDate(r["Date"]),
                        Amount = SafeDecimal(r["Amount"]),
                        Source = (r.Table.Columns.Contains("Source")
                            ? (r["Source"]?.ToString() ?? "")
                            : "").Trim()
                    })
                    .Where(x =>
                        string.Equals(x.Source, "Przelew", StringComparison.OrdinalIgnoreCase) ||
                        x.Source.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));

                var byCategory = new List<(string, decimal)>
                {
                    ("Transfery", total)
                };

                var trend = BuildTrendList(rows.Select(r => (r.Date, r.Amount)), from, to, bucket, false);

                var byAccount = rows
                    .GroupBy(r => r.Source)
                    .Select(g => (string.IsNullOrWhiteSpace(g.Key) ? "Przelew" : g.Key,
                                  g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
                    .ToList();

                var weekdays = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), false);
                var order = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = order
                    .Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                return (total, 0m, 0m, byCategory, trend, byAccount, byWeekday);
            }

            // Domyœlnie: wydatki
            {
                var dt = DatabaseService.GetExpenses(_userId, from, to);
                var rows = dt.AsEnumerable()
                    .Select(r => new
                    {
                        Date = SafeDate(r["Date"]),
                        Amount = SafeDecimal(r["Amount"]),
                        Category = (r.Table.Columns.Contains("CategoryName")
                            ? (r["CategoryName"]?.ToString() ?? "(brak)")
                            : "(brak)").Trim(),
                        AccountId = SafeNullableInt(r["AccountId"])
                    })
                    .ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));

                var byCategory = rows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
                    .ToList();

                var trend = BuildTrendList(rows.Select(r => (r.Date, r.Amount)), from, to, bucket, true);

                var accounts = DatabaseService.GetAccounts(_userId)
                    .ToDictionary(
                        a => a.Id,
                        a => string.IsNullOrWhiteSpace(a.AccountName)
                            ? (a.BankName ?? $"Konto {a.Id}")
                            : a.AccountName);

                string MapAcc(int? id) =>
                    id.HasValue
                        ? (accounts.TryGetValue(id.Value, out var n) ? n : $"Konto {id.Value}")
                        : "Gotówka / brak konta";

                var byAccount = rows
                    .GroupBy(x => MapAcc(x.AccountId))
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
                    .ToList();

                var weekdays = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), true);
                var order = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = order
                    .Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                return (total, 0m, total, byCategory, trend, byAccount, byWeekday);
            }
        }

        private List<(string Label, decimal Value)> BuildTrendList(
            IEnumerable<(DateTime Date, decimal Amount)> items,
            DateTime? from,
            DateTime? to,
            string bucket,
            bool? isExpense)
        {
            var list = items.ToList();
            if (list.Count == 0) return new() { ("-", 0m) };

            DateTime start = from ?? list.Min(x => x.Date).Date;
            DateTime end = to ?? list.Max(x => x.Date).Date;

            var buckets = new List<(string Label, DateTime KeyStart, DateTime KeyEnd)>();

            if (bucket == "day")
            {
                for (var d = start; d <= end; d = d.AddDays(1))
                    buckets.Add((d.ToString("dd.MM"), d, d));
            }
            else if (bucket == "week")
            {
                var cur = start.AddDays(-(((int)start.DayOfWeek + 6) % 7)); // poniedzia³ek
                while (cur <= end)
                {
                    var s = cur;
                    var e = cur.AddDays(6);
                    buckets.Add(($"{s:dd.MM}-{e:dd.MM}", s, e));
                    cur = cur.AddDays(7);
                }
            }
            else // month
            {
                var cur = new DateTime(start.Year, start.Month, 1);
                while (cur <= end)
                {
                    var s = cur;
                    var e = cur.AddMonths(1).AddDays(-1);
                    buckets.Add(($"{s:MM.yyyy}", s, e));
                    cur = cur.AddMonths(1);
                }
            }

            var result = new List<(string, decimal)>();

            foreach (var b in buckets)
            {
                var sum = list
                    .Where(x => x.Date.Date >= b.KeyStart.Date && x.Date.Date <= b.KeyEnd.Date)
                    .Sum(x => x.Amount);

                if (isExpense == true) sum = -Math.Abs(sum);
                else if (isExpense == false) sum = Math.Abs(sum);

                result.Add((b.Label, sum)); // Poprawka: przekazujemy krotkê zamiast dwóch argumentów
            }

            return result;
        }

        private Dictionary<DayOfWeek, decimal> GroupByWeekday(
            IEnumerable<(DateTime Date, decimal Amount)> items,
            bool? isExpense)
        {
            var dict = new Dictionary<DayOfWeek, decimal>();

            foreach (var (date, amountRaw) in items)
            {
                var amount = amountRaw;
                if (isExpense == true) amount = -Math.Abs(amountRaw);
                else if (isExpense == false) amount = Math.Abs(amountRaw);

                var d = date.DayOfWeek;
                if (!dict.TryGetValue(d, out var cur)) cur = 0m;
                dict[d] = cur + amount;
            }

            return dict;
        }

        private static string PolishShortDayName(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "Pn",
            DayOfWeek.Tuesday => "Wt",
            DayOfWeek.Wednesday => "Œr",
            DayOfWeek.Thursday => "Cz",
            DayOfWeek.Friday => "Pt",
            DayOfWeek.Saturday => "So",
            DayOfWeek.Sunday => "Nd",
            _ => d.ToString()
        };

        private static DateTime SafeDate(object? o)
        {
            if (o == null || o is DBNull) return DateTime.Today;
            if (o is DateTime dt) return dt;
            return DateTime.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture),
                out var parsed)
                ? parsed
                : DateTime.Today;
        }

        private static decimal SafeDecimal(object? o)
        {
            if (o == null || o is DBNull) return 0m;
            if (o is decimal d) return d;
            if (o is double dbl) return (decimal)dbl;
            if (o is float fl) return (decimal)fl;

            return decimal.TryParse(
                Convert.ToString(o, CultureInfo.InvariantCulture),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : 0m;
        }

        private static int? SafeNullableInt(object? o)
        {
            if (o == null || o is DBNull) return null;
            if (o is int i) return i;
            return int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out var parsed)
                ? parsed
                : null;
        }
    }
}
