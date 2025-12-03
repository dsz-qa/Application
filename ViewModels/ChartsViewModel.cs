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
using SkiaSharp;

namespace Finly.ViewModels
{
    public class ChartsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public enum Period { Today, Week, Month, Year, All }
        public enum Mode { Expenses, Incomes, Transfer, Cashflow }

        private Period _selectedPeriod = Period.Month;
        public Period SelectedPeriod { get => _selectedPeriod; set { if (_selectedPeriod != value) { _selectedPeriod = value; Raise(nameof(SelectedPeriod)); _useCustomRange = false; LoadStatistics(); } } }
        private Mode _selectedMode = Mode.Expenses;
        public Mode SelectedMode { get => _selectedMode; set { if (_selectedMode != value) { _selectedMode = value; Raise(nameof(SelectedMode)); LoadStatistics(); } } }

        public ObservableCollection<ISeries> CategoriesSeries { get; } = new();
        public ObservableCollection<ISeries> TrendSeries { get; } = new();
        public ObservableCollection<ISeries> AccountsSeries { get; } = new();
        public ObservableCollection<ISeries> WeekdaySeries { get; } = new();
        // NEW: envelopes-only series
        public ObservableCollection<ISeries> EnvelopesSeries { get; } = new();
        public ObservableCollection<ISeries> FreeCashSeries { get; } = new();
        public ObservableCollection<ISeries> SavedCashSeries { get; } = new();

        public ObservableCollection<Axis> TrendXAxes { get; } = new();
        public ObservableCollection<Axis> TrendYAxes { get; } = new();
        public ObservableCollection<Axis> AccountsXAxes { get; } = new();
        public ObservableCollection<Axis> AccountsYAxes { get; } = new();
        public ObservableCollection<Axis> WeekdayXAxes { get; } = new();
        public ObservableCollection<Axis> WeekdayYAxes { get; } = new();
        // NEW: envelopes axes
        public ObservableCollection<Axis> EnvelopesXAxes { get; } = new();
        public ObservableCollection<Axis> EnvelopesYAxes { get; } = new();
        public ObservableCollection<Axis> FreeCashXAxes { get; } = new();
        public ObservableCollection<Axis> FreeCashYAxes { get; } = new();
        public ObservableCollection<Axis> SavedCashXAxes { get; } = new();
        public ObservableCollection<Axis> SavedCashYAxes { get; } = new();

        public ObservableCollection<string> TrendLabels { get; } = new();
        public ObservableCollection<string> AccountLabels { get; } = new();
        public ObservableCollection<string> WeekdayLabels { get; } = new();
        // NEW: envelope labels
        public ObservableCollection<string> EnvelopeLabels { get; } = new();
        public ObservableCollection<string> FreeCashLabels { get; } = new();
        public ObservableCollection<string> SavedCashLabels { get; } = new();

        private readonly int _userId;
        public ChartsViewModel()
        {
            _userId = UserService.GetCurrentUserId();
            try { DatabaseService.DataChanged += (_, __) => LoadStatistics(); } catch { }
            LoadStatistics();
        }

        public void SetPeriod(string period) { if (Enum.TryParse(period, true, out Period p)) SelectedPeriod = p; }
        public void SetMode(string mode) { if (Enum.TryParse(mode, true, out Mode m)) SelectedMode = m; }
        public void SetCustomRange(DateTime start, DateTime end) { _customFrom = start.Date; _customTo = end.Date; _useCustomRange = true; LoadStatistics(); }

        private bool _useCustomRange; private DateTime? _customFrom; private DateTime? _customTo;

        private (DateTime? From, DateTime? To, string Bucket) ResolveRange()
        {
            if (_useCustomRange && _customFrom.HasValue && _customTo.HasValue)
            { var from = _customFrom.Value.Date; var to = _customTo.Value.Date; var span = (to - from).TotalDays; string bucket = span <=7 ? "day" : span <=35 ? "week" : "month"; return (from, to, bucket); }
            var today = DateTime.Today;
            return SelectedPeriod switch
            { Period.Today => (today, today, "day"), Period.Week => (today.AddDays(-6), today, "day"), Period.Month => (new DateTime(today.Year, today.Month,1), new DateTime(today.Year, today.Month,1).AddMonths(1).AddDays(-1), "week"), Period.Year => (new DateTime(today.Year,1,1), new DateTime(today.Year,12,31), "month"), _ => (null, null, "month") };
        }

        public void LoadStatistics()
        {
            CategoriesSeries.Clear(); TrendSeries.Clear(); AccountsSeries.Clear(); WeekdaySeries.Clear(); EnvelopesSeries.Clear(); FreeCashSeries.Clear(); SavedCashSeries.Clear();
            TrendLabels.Clear(); AccountLabels.Clear(); WeekdayLabels.Clear(); EnvelopeLabels.Clear(); FreeCashLabels.Clear(); SavedCashLabels.Clear();
            TrendXAxes.Clear(); TrendYAxes.Clear(); AccountsXAxes.Clear(); AccountsYAxes.Clear(); WeekdayXAxes.Clear(); WeekdayYAxes.Clear(); EnvelopesXAxes.Clear(); EnvelopesYAxes.Clear(); FreeCashXAxes.Clear(); FreeCashYAxes.Clear(); SavedCashXAxes.Clear(); SavedCashYAxes.Clear();

            var (from, to, bucket) = ResolveRange();
            var white = new SolidColorPaint(SKColors.White);

            try
            {
                var data = BuildAggregates(from, to, bucket);

                // Pie
                if (data.ByCategory.Count ==0)
                {
                    CategoriesSeries.Add(new PieSeries<double> { Name = "Brak danych", Values = new[] {1d }, DataLabelsPaint = white });
                }
                else
                {
                    foreach (var cat in data.ByCategory)
                    {
                        CategoriesSeries.Add(new PieSeries<double>
                        {
                            Name = cat.Name,
                            Values = new[] { (double)cat.Sum },
                            DataLabelsPaint = white
                        });
                    }
                }

                // Trend
                if (data.Trend.Count ==0)
                {
                    TrendLabels.Add("-");
                    TrendSeries.Add(new LineSeries<double> { Values = new[] {0d }, GeometrySize =8, Fill = null, DataLabelsPaint = white });
                }
                else
                {
                    foreach (var t in data.Trend) TrendLabels.Add(t.Label);
                    TrendSeries.Add(new LineSeries<double> { Values = data.Trend.Select(t => (double)t.Value).ToArray(), GeometrySize =8, Fill = null, DataLabelsPaint = white });
                }

                TrendXAxes.Add(new Axis { Labels = TrendLabels.ToArray(), LabelsRotation =45, Name = "Okres", LabelsPaint = white, NamePaint = white });
                TrendYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Accounts (free/saved cash + bank accounts)
                if (data.ByAccount.Count ==0)
                {
                    AccountLabels.Add("-");
                    AccountsSeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white });
                }
                else
                {
                    foreach (var a in data.ByAccount) AccountLabels.Add(a.Name);
                    AccountsSeries.Add(new ColumnSeries<double> { Values = data.ByAccount.Select(a => (double)a.Sum).ToArray(), DataLabelsPaint = white });
                }
                AccountsXAxes.Add(new Axis { Labels = AccountLabels.ToArray(), Name = SelectedMode == Mode.Incomes ? "èrÛd≥a" : "Konta / gotÛwka", LabelsPaint = white, NamePaint = white });
                AccountsYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Envelopes
                if (data.ByEnvelope.Count ==0)
                {
                    EnvelopeLabels.Add("-");
                    EnvelopesSeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white });
                }
                else
                {
                    foreach (var e in data.ByEnvelope) EnvelopeLabels.Add(e.Name);
                    EnvelopesSeries.Add(new ColumnSeries<double> { Values = data.ByEnvelope.Select(e => (double)e.Sum).ToArray(), DataLabelsPaint = white });
                }
                EnvelopesXAxes.Add(new Axis { Labels = EnvelopeLabels.ToArray(), Name = "Koperty", LabelsPaint = white, NamePaint = white });
                EnvelopesYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Free cash
                FreeCashLabels.Add("Wolna gotÛwka");
                FreeCashSeries.Add(new ColumnSeries<double> { Values = new[] { (double)data.TotalFreeCash }, DataLabelsPaint = white });
                FreeCashXAxes.Add(new Axis { Labels = FreeCashLabels.ToArray(), LabelsPaint = white, NamePaint = white });
                FreeCashYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Saved cash
                SavedCashLabels.Add("Od≥oøona gotÛwka");
                SavedCashSeries.Add(new ColumnSeries<double> { Values = new[] { (double)data.TotalSavedCash }, DataLabelsPaint = white });
                SavedCashXAxes.Add(new Axis { Labels = SavedCashLabels.ToArray(), LabelsPaint = white, NamePaint = white });
                SavedCashYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Weekday
                if (data.ByWeekday.Count ==0)
                {
                    WeekdayLabels.Add("-");
                    WeekdaySeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white });
                }
                else
                {
                    foreach (var d in data.ByWeekday) WeekdayLabels.Add(d.Name);
                    WeekdaySeries.Add(new ColumnSeries<double> { Values = data.ByWeekday.Select(d => (double)d.Sum).ToArray(), DataLabelsPaint = white });
                }
                WeekdayXAxes.Add(new Axis { Labels = WeekdayLabels.ToArray(), Name = "DzieÒ tygodnia", LabelsPaint = white, NamePaint = white });
                WeekdayYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });
            }
            catch
            {
                CategoriesSeries.Add(new PieSeries<double> { Name = "Brak danych", Values = new[] {1d }, DataLabelsPaint = white });
                TrendLabels.Add("-"); TrendSeries.Add(new LineSeries<double> { Values = new[] {0d }, Fill = null, DataLabelsPaint = white });
                TrendXAxes.Add(new Axis { Labels = TrendLabels.ToArray(), LabelsPaint = white, NamePaint = white }); TrendYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });
                AccountLabels.Add("-"); AccountsSeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white }); AccountsXAxes.Add(new Axis { Labels = AccountLabels.ToArray(), LabelsPaint = white, NamePaint = white }); AccountsYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });
                EnvelopeLabels.Add("-"); EnvelopesSeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white }); EnvelopesXAxes.Add(new Axis { Labels = EnvelopeLabels.ToArray(), LabelsPaint = white, NamePaint = white }); EnvelopesYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });
                FreeCashLabels.Add("-"); FreeCashSeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white }); FreeCashXAxes.Add(new Axis { Labels = FreeCashLabels.ToArray(), LabelsPaint = white, NamePaint = white }); FreeCashYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });
                SavedCashLabels.Add("-"); SavedCashSeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white }); SavedCashXAxes.Add(new Axis { Labels = SavedCashLabels.ToArray(), LabelsPaint = white, NamePaint = white }); SavedCashYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });
                WeekdayLabels.Add("-"); WeekdaySeries.Add(new ColumnSeries<double> { Values = new[] {0d }, DataLabelsPaint = white }); WeekdayXAxes.Add(new Axis { Labels = WeekdayLabels.ToArray(), LabelsPaint = white, NamePaint = white }); WeekdayYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });
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
                            r.RelativeItem().Text("Finly ñ Statystyki").SemiBold().FontSize(18);
                            r.ConstantItem(220).AlignRight().Text($"{modeText} ñ {periodText}");
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
                            col.Item().PaddingTop(10).Text("Podzia≥ wg kategorii").Bold();
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

                            // Konta (wolna/od≥oøona/bank)
                            col.Item().PaddingTop(10)
                               .Text(SelectedMode == Mode.Incomes ? "èrÛd≥a" : "Konta / gotÛwka")
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

                            // Koperty
                            col.Item().PaddingTop(10).Text("Koperty").Bold();
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

                                foreach (var r2 in data.ByEnvelope)
                                {
                                    t.Cell().Text(r2.Name);
                                    t.Cell().Text(r2.Sum.ToString("N2"));
                                }
                            });

                            // Wolna gotÛwka
                            col.Item().PaddingTop(10).Text("Wolna gotÛwka").Bold();
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

                                t.Cell().Text("Wolna gotÛwka");
                                t.Cell().Text(data.TotalFreeCash.ToString("N2"));
                            });

                            // Od≥oøona gotÛwka
                            col.Item().PaddingTop(10).Text("Od≥oøona gotÛwka").Bold();
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

                                t.Cell().Text("Od≥oøona gotÛwka");
                                t.Cell().Text(data.TotalSavedCash.ToString("N2"));
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
                                    h.Cell().Text("DzieÒ").Bold();
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
                ToastService.Error($"B≥πd eksportu PDF: {ex.Message}");
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
                w.WriteLine("Podzia≥ wg kategorii");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByCategory)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Trend");
                w.WriteLine("Okres;Suma");
                foreach (var r in data.Trend)
                    w.WriteLine($"{Escape(r.Label)};{r.Value.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                var hdr = SelectedMode == Mode.Incomes ? "èrÛd≥a" : "Konta / gotÛwka";
                w.WriteLine(hdr);
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByAccount)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Koperty");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByEnvelope)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Wolna gotÛwka");
                w.WriteLine("Nazwa;Suma");
                w.WriteLine("Wolna gotÛwka;" + data.TotalFreeCash.ToString("N2", CultureInfo.InvariantCulture));

                w.WriteLine();
                w.WriteLine("Od≥oøona gotÛwka");
                w.WriteLine("Nazwa;Suma");
                w.WriteLine("Od≥oøona gotÛwka;" + data.TotalSavedCash.ToString("N2", CultureInfo.InvariantCulture));

                w.WriteLine();
                w.WriteLine("Dni tygodnia");
                w.WriteLine("DzieÒ;Suma");
                foreach (var r in data.ByWeekday)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                ToastService.Success($"Zapisano CSV: {sfd.FileName}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"B≥πd eksportu CSV: {ex.Message}");
            }
        }

        private static string Escape(string? s) => string.IsNullOrEmpty(s) ? "" : s.Replace(";", ",");
        private string DescribePeriod(DateTime? from, DateTime? to) { if (from == null && to == null) return "Ca≥y czas"; if (from == to) return from?.ToString("yyyy-MM-dd") ?? ""; return $"{from:yyyy-MM-dd} ñ {to:yyyy-MM-dd}"; }
        // BuildAggregates, BuildTrendList, GroupByWeekday, Safe* methods updated below

        // ===== AGREGACJE =====
        private (decimal SummaryTotal, decimal TotalIncomes, decimal TotalExpenses,
                 List<(string Name, decimal Sum)> ByCategory,
                 List<(string Label, decimal Value)> Trend,
                 List<(string Name, decimal Sum)> ByAccount,
                 List<(string Name, decimal Sum)> ByEnvelope,
                 List<(string Name, decimal Sum)> ByWeekday,
                 decimal TotalFreeCash,
                 decimal TotalSavedCash)
            BuildAggregates(DateTime? from, DateTime? to, string bucket)
        {
            if (SelectedMode == Mode.Cashflow)
            {
                var exp = DatabaseService.GetExpenses(_userId, from, to).AsEnumerable()
                    .Select(r => new { Date = SafeDate(r["Date"]), Amount = SafeDecimal(r["Amount"]) }).ToList();
                var inc = DatabaseService.GetIncomes(_userId, from, to).AsEnumerable()
                    .Select(r => new { Date = SafeDate(r["Date"]), Amount = SafeDecimal(r["Amount"]) }).ToList();

                var incomeTotal = inc.Sum(x => Math.Abs(x.Amount));
                var expenseTotal = exp.Sum(x => Math.Abs(x.Amount));
                var byCategory = new List<(string, decimal)> { ("Przychody", incomeTotal), ("Wydatki", expenseTotal) };
                var merged = new List<(DateTime Date, decimal Amount)>();
                merged.AddRange(inc.Select(x => (x.Date, Math.Abs(x.Amount))));
                merged.AddRange(exp.Select(x => (x.Date, -Math.Abs(x.Amount))));
                var trend = BuildTrendList(merged, from, to, bucket, null);
                var byAccount = new List<(string, decimal)> { ("Przychody", incomeTotal), ("Wydatki", expenseTotal) };
                var byEnvelope = new List<(string, decimal)>(); // cashflow: brak sensownego rozbicia na koperty
                var weekdays = GroupByWeekday(merged, null);
                var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
                var byWeekday = order.Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v :0m)).ToList();
                var summary = incomeTotal - expenseTotal;
                return (summary, incomeTotal, expenseTotal, byCategory, trend, byAccount, byEnvelope, byWeekday, 0m, 0m);
            }

            if (SelectedMode == Mode.Incomes)
            {
                var dt = DatabaseService.GetIncomes(_userId, from, to);
                var rows = dt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Category = (r.Table.Columns.Contains("CategoryName") ? (r["CategoryName"]?.ToString() ?? "(brak)") : "(brak)").Trim(),
                    Source = (r.Table.Columns.Contains("Source") ? (r["Source"]?.ToString() ?? "Przychody") : "Przychody").Trim()
                }).ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));
                var byCategory = rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2).ToList();
                var trend = BuildTrendList(rows.Select(r => (r.Date, r.Amount)), from, to, bucket, false);
                var byAccount = rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Source) ? "Przychody" : x.Source)
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2).ToList();
                var byEnvelope = new List<(string, decimal)>(); // przychody nie majπ kopert
                var weekdays = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), false);
                var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
                var byWeekday = order.Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v :0m)).ToList();
                return (total, total,0m, byCategory, trend, byAccount, byEnvelope, byWeekday,0m,0m);
            }

            if (SelectedMode == Mode.Transfer)
            {
                var dt = DatabaseService.GetIncomes(_userId, from, to);
                var rows = dt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Source = (r.Table.ColumnsContains("Source") ? (r["Source"]?.ToString() ?? string.Empty) : string.Empty).Trim()
                }).Where(x => string.Equals(x.Source, "Przelew", StringComparison.OrdinalIgnoreCase) || x.Source.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase)).ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));
                var byCategory = new List<(string, decimal)> { ("Transfery", total) };
                var trend = BuildTrendList(rows.Select(r => (r.Date, r.Amount)), from, to, bucket, false);
                var byAccount = rows.GroupBy(r => r.Source).Select(g => (string.IsNullOrWhiteSpace(g.Key) ? "Przelew" : g.Key, g.Sum(x => Math.Abs(x.Amount)))).OrderByDescending(x => x.Item2).ToList();
                var byEnvelope = new List<(string, decimal)>();
                var weekdays = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), false);
                var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
                var byWeekday = order.Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v :0m)).ToList();
                return (total,0m,0m, byCategory, trend, byAccount, byEnvelope, byWeekday,0m,0m);
            }

            // Expenses
            {
                var dt = DatabaseService.GetExpenses(_userId, from, to);
                var rows = dt.AsEnumerable().Select r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Category = (r.Table.Columns.Contains("CategoryName") ? (r["CategoryName"]?.ToString() ?? "(brak)") : "(brak)").Trim(),
                    AccountText = (r.Table.ColumnsContains("Account") ? (r["Account"]?.ToString() ?? string.Empty) : string.Empty).Trim(),
                    AccountId = SafeNullableInt(r["AccountId"]) // may be null
                }).ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));
                var byCategory = rows.GroupBy x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select g => (g.Key, g.Sum(x => Math.Abs(x.Amount)))
                    .OrderByDescending x => x.Item2).ToList();
                var trend = BuildTrendList(rows.Select(r => (r.Date, r.Amount)), from, to, bucket, true);

                // Split into accounts (free/saved cash + bank accounts) and envelopes by AccountText
                var byAccountDict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                var byEnvelopeDict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows)
                {
                    var amount = Math.Abs(row.Amount);
                    var acc = row.AccountText ?? string.Empty;

                    if (acc.StartsWith("Koperta:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = acc.Substring("Koperta:".Length).Trim();
                        if (string.IsNullOrWhiteSpace(name)) name = "(bez nazwy)";
                        byEnvelopeDict[name] = byEnvelopeDict.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (acc.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = acc.Substring("Konto:".Length).Trim();
                        if (string.IsNullOrWhiteSpace(name)) name = "(konto)";
                        byAccountDict[name] = byAccountDict.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (string.Equals(acc, "Wolna gotÛwka", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = "Wolna gotÛwka";
                        byAccountDict[name] = byAccountDict.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (string.Equals(acc, "Od≥oøona gotÛwka", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = "Od≥oøona gotÛwka";
                        byAccountDict[name] = byAccountDict.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else
                    {
                        // fallback: group by resolved bank account name if AccountId exists, otherwise bucket as "Inne"
                        string name;
                        if (row.AccountId is int id)
                        {
                            var accounts = DatabaseService.GetAccounts(_userId).ToDictionary(a => a.Id, a => string.IsNullOrWhiteSpace(a.AccountName) ? (a.BankName ?? $"Konto {a.Id}") : a.AccountName);
                            name = accounts.TryGetValue(id, out var n) ? n : $"Konto {id}";
                        }
                        else
                        {
                            name = string.IsNullOrWhiteSpace(acc) ? "Inne" : acc;
                        }
                        byAccountDict[name] = byAccountDict.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                }

                var byAccount = byAccountDict.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList();
                var byEnvelope = byEnvelopeDict.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList();

                var totalFree = byAccountDict.TryGetValue("Wolna gotÛwka", out var tf) ? tf :0m;
                var totalSaved = byAccountDict.TryGetValue("Od≥oøona gotÛwka", out var ts) ? ts :0m;

                var weekdays = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), true);
                var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
                var byWeekday = order.Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v :0m)).ToList();
                return (total,0m, total, byCategory, trend, byAccount, byEnvelope, byWeekday, totalFree, totalSaved);
            }
        }

        private List<(string Label, decimal Value)> BuildTrendList(IEnumerable<(DateTime Date, decimal Amount)> items, DateTime? from, DateTime? to, string bucket, bool? isExpense)
        {
            var list = items.ToList(); if (list.Count ==0) return new() { ("-",0m) };
            DateTime start = from ?? list.Min(x => x.Date).Date; DateTime end = to ?? list.Max(x => x.Date).Date;
            var buckets = new List<(string Label, DateTime KeyStart, DateTime KeyEnd)>();
            if (bucket == "day") { for (var d = start; d <= end; d = d.AddDays(1)) buckets.Add((d.ToString("dd.MM"), d, d)); }
            else if (bucket == "week") { var cur = start.AddDays(-(((int)start.DayOfWeek +6) %7)); while (cur <= end) { var s = cur; var e = cur.AddDays(6); buckets.Add(($"{s:dd.MM}-{e:dd.MM}", s, e)); cur = cur.AddDays(7); } }
            else { var cur = new DateTime(start.Year, start.Month,1); while (cur <= end) { var s = cur; var e = cur.AddMonths(1).AddDays(-1); buckets.Add(($"{s:MM.yyyy}", s, e)); cur = cur.AddMonths(1); } }
            var result = new List<(string, decimal)>();
            foreach (var b in buckets)
            {
                var sum = list.Where(x => x.Date.Date >= b.KeyStart.Date && x.Date.Date <= b.KeyEnd.Date).Sum(x => x.Amount);
                if (isExpense == true) sum = -Math.Abs(sum); else if (isExpense == false) sum = Math.Abs(sum);
                result.Add((b.Label, sum));
            }
            return result;
        }

        private Dictionary<DayOfWeek, decimal> GroupByWeekday(IEnumerable<(DateTime Date, decimal Amount)> items, bool? isExpense)
        {
            var dict = new Dictionary<DayOfWeek, decimal>();
            foreach (var (date, amountRaw) in items)
            {
                var amount = amountRaw; if (isExpense == true) amount = -Math.Abs(amountRaw); else if (isExpense == false) amount = Math.Abs(amountRaw);
                var d = date.DayOfWeek; if (!dict.TryGetValue(d, out var cur)) cur =0m; dict[d] = cur + amount;
            }
            return dict;
        }

        private static string PolishShortDayName(DayOfWeek d) => d switch
        { DayOfWeek.Monday => "Pn", DayOfWeek.Tuesday => "Wt", DayOfWeek.Wednesday => "år", DayOfWeek.Thursday => "Cz", DayOfWeek.Friday => "Pt", DayOfWeek.Saturday => "So", DayOfWeek.Sunday => "Nd", _ => d.ToString() };
        private static DateTime SafeDate(object? o) { if (o == null || o is DBNull) return DateTime.Today; if (o is DateTime dt) return dt; return DateTime.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out var parsed) ? parsed : DateTime.Today; }
        private static decimal SafeDecimal(object? o)
        {
            if (o == null || o is DBNull) return0m;
            if (o is decimal d) return d;
            if (o is double dbl) return (decimal)dbl;
            if (o is float fl) return (decimal)fl;
            return decimal.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed :0m;
        }
        private static int? SafeNullableInt(object? o) { if (o == null || o is DBNull) return null; if (o is int i) return i; return int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out var parsed) ? parsed : (int?)null; }
    }
}
