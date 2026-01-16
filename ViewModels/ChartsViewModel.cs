using Finly.Services;
using Finly.Services.Features;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Finly.ViewModels
{
    public class ChartsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public enum Period { Today, Week, Month, Year, All }
        public enum Mode { All, Expenses, Incomes, Transfer }

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

        private Mode _selectedMode = Mode.All;
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

        public ObservableCollection<ISeries> CategoriesSeries { get; } = new();
        public ObservableCollection<ISeries> TrendSeries { get; } = new();
        public ObservableCollection<ISeries> WeekdaySeries { get; } = new();
        public ObservableCollection<ISeries> EnvelopesSeries { get; } = new();
        public ObservableCollection<ISeries> BankAccountsSeries { get; } = new();
        public ObservableCollection<ISeries> FreeCashSeries { get; } = new();
        public ObservableCollection<ISeries> SavedCashSeries { get; } = new();
        public ObservableCollection<ISeries> AmountBucketsSeries { get; } = new();

        public ObservableCollection<Axis> TrendXAxes { get; } = new();
        public ObservableCollection<Axis> TrendYAxes { get; } = new();
        public ObservableCollection<Axis> WeekdayXAxes { get; } = new();
        public ObservableCollection<Axis> WeekdayYAxes { get; } = new();
        public ObservableCollection<Axis> EnvelopesXAxes { get; } = new();
        public ObservableCollection<Axis> EnvelopesYAxes { get; } = new();
        public ObservableCollection<Axis> BankAccountsXAxes { get; } = new();
        public ObservableCollection<Axis> BankAccountsYAxes { get; } = new();
        public ObservableCollection<Axis> FreeCashXAxes { get; } = new();
        public ObservableCollection<Axis> FreeCashYAxes { get; } = new();
        public ObservableCollection<Axis> SavedCashXAxes { get; } = new();
        public ObservableCollection<Axis> SavedCashYAxes { get; } = new();
        public ObservableCollection<Axis> AmountBucketsXAxes { get; } = new();
        public ObservableCollection<Axis> AmountBucketsYAxes { get; } = new();

        public ObservableCollection<string> TrendLabels { get; } = new();
        public ObservableCollection<string> WeekdayLabels { get; } = new();
        public ObservableCollection<string> EnvelopeLabels { get; } = new();
        public ObservableCollection<string> BankAccountLabels { get; } = new();
        public ObservableCollection<string> FreeCashLabels { get; } = new();
        public ObservableCollection<string> SavedCashLabels { get; } = new();
        public ObservableCollection<string> AmountBucketsLabels { get; } = new();

        private readonly int _userId;
        private readonly CultureInfo _pl = CultureInfo.GetCultureInfo("pl-PL");

        private SolidColorPaint _fg = new SolidColorPaint(SKColors.White);
        private SolidColorPaint _grid = new SolidColorPaint(new SKColor(255, 255, 255, 28));

        private bool _useCustomRange;
        private DateTime? _customFrom;
        private DateTime? _customTo;

        public ChartsViewModel()
        {
            _userId = UserService.GetCurrentUserId();

            try
            {
                DatabaseService.DataChanged += (_, __) => LoadStatistics();
            }
            catch
            {
                // VM ma działać nawet jeśli event nie jest dostępny w danym kontekście
            }

            LoadStatistics();
        }

        private string Pln(double v)
            => (v < 0 ? "-" : "") + Math.Abs(v).ToString("N0", _pl) + " zł";

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
                _ => (null, null, "month")
            };
        }

        public void LoadStatistics()
        {
            CategoriesSeries.Clear();
            TrendSeries.Clear();
            WeekdaySeries.Clear();
            EnvelopesSeries.Clear();
            BankAccountsSeries.Clear();
            FreeCashSeries.Clear();
            SavedCashSeries.Clear();
            AmountBucketsSeries.Clear();

            TrendLabels.Clear();
            WeekdayLabels.Clear();
            EnvelopeLabels.Clear();
            BankAccountLabels.Clear();
            FreeCashLabels.Clear();
            SavedCashLabels.Clear();
            AmountBucketsLabels.Clear();

            TrendXAxes.Clear();
            TrendYAxes.Clear();
            WeekdayXAxes.Clear();
            WeekdayYAxes.Clear();
            EnvelopesXAxes.Clear();
            EnvelopesYAxes.Clear();
            BankAccountsXAxes.Clear();
            BankAccountsYAxes.Clear();
            FreeCashXAxes.Clear();
            FreeCashYAxes.Clear();
            SavedCashXAxes.Clear();
            SavedCashYAxes.Clear();
            AmountBucketsXAxes.Clear();
            AmountBucketsYAxes.Clear();

            var (from, to, bucket) = ResolveRange();

            _fg = new SolidColorPaint(SKColors.White);
            _grid = new SolidColorPaint(new SKColor(255, 255, 255, 28));

            SKColor accent = SelectedMode switch
            {
                Mode.Expenses => new SKColor(244, 67, 54),
                Mode.Incomes => new SKColor(76, 175, 80),
                Mode.Transfer => new SKColor(156, 39, 176),
                _ => new SKColor(255, 152, 0),
            };

            var accentStroke = new SolidColorPaint(accent, 3);
            var accentFill = new SolidColorPaint(new SKColor(accent.Red, accent.Green, accent.Blue, 90));

            try
            {
                var data = BuildAggregates(from, to, bucket);

                // ===== Pie =====
                if (data.ByCategory.Count == 0)
                {
                    CategoriesSeries.Add(new PieSeries<double>
                    {
                        Name = "Brak danych",
                        Values = new[] { 1d },
                        DataLabelsPaint = _fg,
                        DataLabelsFormatter = _ => ""
                    });
                }
                else
                {
                    foreach (var cat in data.ByCategory)
                    {
                        CategoriesSeries.Add(new PieSeries<double>
                        {
                            Name = cat.Name,
                            Values = new[] { (double)cat.Sum },
                            DataLabelsPaint = _fg,
                            DataLabelsFormatter = _ => ""
                        });
                    }
                }

                // ===== Trend =====
                double[] trendValues;
                if (data.Trend.Count == 0 || (data.Trend.Count == 1 && data.Trend[0].Label == "-" && data.Trend[0].Value == 0m))
                {
                    TrendLabels.Add("-");
                    trendValues = new[] { 0d };

                    TrendSeries.Add(new LineSeries<double>
                    {
                        Name = NoDataName(),
                        Values = trendValues,
                        GeometrySize = 0,
                        Fill = null,
                        Stroke = accentStroke,
                        DataLabelsPaint = _fg,
                        DataLabelsPosition = DataLabelsPosition.Top,
                        DataLabelsFormatter = _ => NoDataName()
                    });
                }
                else
                {
                    foreach (var t in data.Trend) TrendLabels.Add(t.Label);
                    trendValues = data.Trend.Select(t => (double)t.Value).ToArray();

                    TrendSeries.Add(new LineSeries<double>
                    {
                        Values = trendValues,
                        GeometrySize = 8,
                        Fill = accentFill,
                        Stroke = accentStroke,
                        DataLabelsPaint = _fg,
                        DataLabelsPosition = DataLabelsPosition.Top,
                        DataLabelsFormatter = p => Pln(p.Coordinate.PrimaryValue)
                    });
                }

                TrendXAxes.Add(new Axis
                {
                    Labels = TrendLabels.ToArray(),
                    LabelsRotation = 0,
                    LabelsPaint = _fg,
                    SeparatorsPaint = _grid,
                    NamePaint = _fg,
                    TextSize = 12
                });

                ApplyAxisByMode(TrendYAxes, trendValues);

                // ===== Envelopes =====
                BuildColumnChart(
                    data.ByEnvelope,
                    EnvelopeLabels, EnvelopesSeries, EnvelopesXAxes, EnvelopesYAxes,
                    accentStroke, accentFill, maxBarWidth: 46);

                // ===== Weekday =====
                BuildColumnChart(
                    data.ByWeekday,
                    WeekdayLabels, WeekdaySeries, WeekdayXAxes, WeekdayYAxes,
                    accentStroke, accentFill, maxBarWidth: 42);

                // ===== Bank accounts =====
                BuildColumnChart(
                    data.ByBankAccount,
                    BankAccountLabels, BankAccountsSeries, BankAccountsXAxes, BankAccountsYAxes,
                    accentStroke, accentFill, maxBarWidth: 46);

                // ===== Free cash =====
                BuildColumnChart(
                    data.FreeCash,
                    FreeCashLabels, FreeCashSeries, FreeCashXAxes, FreeCashYAxes,
                    accentStroke, accentFill, maxBarWidth: 46);

                // ===== Saved cash =====
                BuildColumnChart(
                    data.SavedCash,
                    SavedCashLabels, SavedCashSeries, SavedCashXAxes, SavedCashYAxes,
                    accentStroke, accentFill, maxBarWidth: 46);

                // ===== Amount buckets =====
                BuildAmountBucketsChart(from, to, accentStroke, accentFill);
            }
            catch
            {
                CategoriesSeries.Add(new PieSeries<double> { Name = "Brak danych", Values = new[] { 1d } });

                TrendLabels.Add("-");
                TrendSeries.Add(new LineSeries<double> { Values = new[] { 0d } });
                TrendXAxes.Add(new Axis { Labels = TrendLabels.ToArray() });
                TrendYAxes.Add(new Axis());

                WeekdayLabels.Add("-");
                WeekdaySeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                WeekdayXAxes.Add(new Axis { Labels = WeekdayLabels.ToArray() });
                WeekdayYAxes.Add(new Axis());

                EnvelopeLabels.Add("-");
                EnvelopesSeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                EnvelopesXAxes.Add(new Axis { Labels = EnvelopeLabels.ToArray() });
                EnvelopesYAxes.Add(new Axis());

                BankAccountLabels.Add("-");
                BankAccountsSeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                BankAccountsXAxes.Add(new Axis { Labels = BankAccountLabels.ToArray() });
                BankAccountsYAxes.Add(new Axis());

                FreeCashLabels.Add("-");
                FreeCashSeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                FreeCashXAxes.Add(new Axis { Labels = FreeCashLabels.ToArray() });
                FreeCashYAxes.Add(new Axis());

                SavedCashLabels.Add("-");
                SavedCashSeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                SavedCashXAxes.Add(new Axis { Labels = SavedCashLabels.ToArray() });
                SavedCashYAxes.Add(new Axis());

                AmountBucketsLabels.Add("-");
                AmountBucketsSeries.Add(new ColumnSeries<double> { Values = new[] { 0d } });
                AmountBucketsXAxes.Add(new Axis { Labels = AmountBucketsLabels.ToArray() });
                AmountBucketsYAxes.Add(new Axis());
            }
        }

        // =========================
        // One unified column builder
        // =========================

        private void BuildColumnChart(
            List<(string Name, decimal Sum)> rows,
            ObservableCollection<string> labels,
            ObservableCollection<ISeries> series,
            ObservableCollection<Axis> xAxes,
            ObservableCollection<Axis> yAxes,
            SolidColorPaint accentStroke,
            SolidColorPaint accentFill,
            double maxBarWidth)
        {
            labels.Clear();
            series.Clear();
            xAxes.Clear();
            yAxes.Clear();

            double[] values;

            if (rows == null || rows.Count == 0)
            {
                labels.Add("-");
                values = new[] { 0d };

                series.Add(new ColumnSeries<double>
                {
                    Values = values,
                    Rx = 6,
                    Ry = 6,
                    MaxBarWidth = maxBarWidth,
                    Fill = accentFill,
                    Stroke = accentStroke,
                    DataLabelsPaint = null
                });
            }
            else
            {
                foreach (var r in rows) labels.Add(r.Name);

                values = rows.Select(r => Sign((double)r.Sum)).ToArray();

                series.Add(new ColumnSeries<double>
                {
                    Values = values,
                    Rx = 6,
                    Ry = 6,
                    MaxBarWidth = maxBarWidth,
                    Fill = accentFill,
                    Stroke = accentStroke,
                    DataLabelsPaint = _fg,
                    DataLabelsPosition = DataLabelsPosition.Top,
                    DataLabelsFormatter = p => Pln(p.Coordinate.PrimaryValue)
                });
            }

            xAxes.Add(new Axis
            {
                Labels = labels.ToArray(),
                LabelsPaint = _fg,
                SeparatorsPaint = _grid,
                NamePaint = _fg,
                TextSize = 12
            });

            ApplyAxisByMode(yAxes, values);
        }

        // =========================
        // Helpers (ONLY ONE COPY)
        // =========================

        private double Sign(double v)
            => SelectedMode == Mode.Expenses ? -Math.Abs(v) : Math.Abs(v);

        private void ApplyAxisByMode(ObservableCollection<Axis> yAxes, double[] values)
        {
            double min = values.Length == 0 ? 0 : values.Min();
            double max = values.Length == 0 ? 0 : values.Max();

            double minLimit, maxLimit;

            if (SelectedMode == Mode.Expenses)
            {
                maxLimit = 0;
                minLimit = Math.Min(0, min);
            }
            else if (SelectedMode == Mode.Incomes || SelectedMode == Mode.Transfer)
            {
                minLimit = 0;
                maxLimit = Math.Max(0, max);
            }
            else
            {
                minLimit = Math.Min(0, min);
                maxLimit = Math.Max(0, max);
            }

            var span = Math.Abs(maxLimit - minLimit);
            if (span < 0.01) span = 1;
            var pad = span * 0.12;

            // headroom na etykiety
            maxLimit += pad;
            minLimit -= pad;

            yAxes.Clear();
            yAxes.Add(new Axis
            {
                MinLimit = minLimit,
                MaxLimit = maxLimit,
                LabelsPaint = _fg,
                SeparatorsPaint = _grid,
                NamePaint = _fg,
                TextSize = 12,
                Labeler = v => Pln(v)
            });
        }

        private string NoDataName() => SelectedMode switch
        {
            Mode.Incomes => "Brak przychodów",
            Mode.Transfer => "Brak transferów",
            Mode.Expenses => "Brak wydatków",
            _ => "Brak danych"
        };

        // =========================
        // Amount buckets
        // =========================

        private void BuildAmountBucketsChart(
            DateTime? from, DateTime? to,
            SolidColorPaint accentStroke, SolidColorPaint accentFill)
        {
            var bucketsData = GetAmountBucketsData(from, to);

            AmountBucketsLabels.Clear();
            AmountBucketsSeries.Clear();
            AmountBucketsXAxes.Clear();
            AmountBucketsYAxes.Clear();

            double[] values;

            if (bucketsData == null || bucketsData.Count == 0 || bucketsData.All(b => b.Sum == 0))
            {
                AmountBucketsLabels.Add("-");
                values = new[] { 0d };

                AmountBucketsSeries.Add(new ColumnSeries<double>
                {
                    Values = values,
                    Rx = 6,
                    Ry = 6,
                    MaxBarWidth = 42,
                    Fill = accentFill,
                    Stroke = accentStroke,
                    DataLabelsPaint = null
                });
            }
            else
            {
                foreach (var b in bucketsData)
                    AmountBucketsLabels.Add(b.Name);

                // to jest histogram liczności -> dodatnie zawsze
                values = bucketsData.Select(b => (double)b.Sum).ToArray();

                AmountBucketsSeries.Add(new ColumnSeries<double>
                {
                    Values = values,
                    Rx = 6,
                    Ry = 6,
                    MaxBarWidth = 42,
                    Fill = accentFill,
                    Stroke = accentStroke,
                    DataLabelsPaint = _fg,
                    DataLabelsPosition = DataLabelsPosition.Top,
                    DataLabelsFormatter = p =>
                    {
                        var v = p.Coordinate.PrimaryValue;
                        if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) return "";
                        return Math.Round(v).ToString("N0", _pl);
                    }
                });
            }

            AmountBucketsXAxes.Add(new Axis
            {
                Labels = AmountBucketsLabels.ToArray(),
                LabelsPaint = _fg,
                SeparatorsPaint = _grid,
                NamePaint = _fg,
                TextSize = 12
            });

            // histogram: od 0 w górę
            ApplyAxisForceZeroToMax(AmountBucketsYAxes, values);
        }

        private void ApplyAxisForceZeroToMax(ObservableCollection<Axis> yAxes, double[] values)
        {
            var max = values.Length == 0 ? 0 : values.Max();
            var maxLimit = Math.Max(0, max);

            var span = Math.Abs(maxLimit - 0);
            if (span < 0.01) span = 1;
            var pad = span * 0.12;

            yAxes.Clear();
            yAxes.Add(new Axis
            {
                MinLimit = 0,
                MaxLimit = maxLimit + pad,
                LabelsPaint = _fg,
                SeparatorsPaint = _grid,
                NamePaint = _fg,
                TextSize = 12,
                Labeler = v => v <= 0 ? "0" : v.ToString("N0", _pl)
            });
        }

        private List<(string Name, decimal Sum)> GetAmountBucketsData(DateTime? from, DateTime? to)
        {
            var amounts = new List<decimal>();

            if (SelectedMode == Mode.All)
            {
                var exp = DatabaseService.GetExpenses(_userId, from, to);
                amounts.AddRange(exp.AsEnumerable().Select(r => Math.Abs(SafeDecimal(r["Amount"]))));

                var inc = DatabaseService.GetIncomes(_userId, from, to);
                amounts.AddRange(inc.AsEnumerable().Select(r => Math.Abs(SafeDecimal(r["Amount"]))));
            }
            else if (SelectedMode == Mode.Incomes || SelectedMode == Mode.Transfer)
            {
                var inc = DatabaseService.GetIncomes(_userId, from, to)
                    .AsEnumerable()
                    .Select(r => Math.Abs(SafeDecimal(r["Amount"])));

                amounts.AddRange(inc);
            }
            else // Expenses
            {
                var exp = DatabaseService.GetExpenses(_userId, from, to);
                amounts.AddRange(exp.AsEnumerable().Select(r => Math.Abs(SafeDecimal(r["Amount"]))));
            }

            return BuildAmountBuckets(amounts);
        }

        private static List<(string Name, decimal Sum)> BuildAmountBuckets(IEnumerable<decimal> amounts)
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in amounts)
            {
                string label =
                    v <= 50 ? "0–50" :
                    v <= 100 ? "50–100" :
                    v <= 200 ? "100–200" :
                    v <= 500 ? "200–500" :
                    v <= 1000 ? "500–1000" :
                    ">1000";

                result[label] = result.TryGetValue(label, out var cur) ? cur + 1 : 1;
            }

            var ordered = new[] { "0–50", "50–100", "100–200", "200–500", "500–1000", ">1000" };
            return ordered.Select(l => (l, result.TryGetValue(l, out var c) ? c : 0m)).ToList();
        }

        // =========================
        // Export PDF / CSV
        // =========================

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
                    Mode.All => "Wszystkie transakcje",
                    Mode.Incomes => "Przychody",
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

                            col.Item().PaddingTop(10).Text("Podział wg kategorii").Bold();
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
                        });

                        page.Footer().AlignRight().Text($"Wygenerowano: {DateTime.Now:yyyy-MM-dd HH:mm}");
                    });
                }).GeneratePdf(sfd.FileName);

                ToastService.Success($"Zapisano PDF: {sfd.FileName}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"Błąd eksportu PDF: {ex.Message}");
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
                w.WriteLine("Podział wg kategorii");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByCategory)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                ToastService.Success($"Zapisano CSV: {sfd.FileName}");
            }
            catch (Exception ex)
            {
                ToastService.Error($"Błąd eksportu CSV: {ex.Message}");
            }
        }

        private static string Escape(string? s) => string.IsNullOrEmpty(s) ? "" : s.Replace(";", ",");

        private string DescribePeriod(DateTime? from, DateTime? to)
        {
            if (from == null && to == null) return "Cały czas";
            if (from == to) return from?.ToString("yyyy-MM-dd") ?? "";
            return $"{from:yyyy-MM-dd} – {to:yyyy-MM-dd}";
        }

        // =========================
        // Aggregates (core)
        // =========================

        private (decimal SummaryTotal, decimal TotalIncomes, decimal TotalExpenses,
                 List<(string Name, decimal Sum)> ByCategory,
                 List<(string Label, decimal Value)> Trend,
                 List<(string Name, decimal Sum)> ByBankAccount,
                 List<(string Name, decimal Sum)> FreeCash,
                 List<(string Name, decimal Sum)> SavedCash,
                 List<(string Name, decimal Sum)> ByEnvelope,
                 List<(string Name, decimal Sum)> ByWeekday)
            BuildAggregates(DateTime? from, DateTime? to, string bucket)
        {
            var snapshot = DatabaseService.GetMoneySnapshot(_userId);

            if (SelectedMode == Mode.All)
            {
                var expDt = DatabaseService.GetExpenses(_userId, from, to);
                var expRows = expDt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = Math.Abs(SafeDecimal(r["Amount"])),
                    Category = (r.Table.Columns.Contains("CategoryName") ? (r["CategoryName"]?.ToString() ?? "(brak)") : "(brak)").Trim()
                }).ToList();

                var incDt = DatabaseService.GetIncomes(_userId, from, to);
                var incRows = incDt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = Math.Abs(SafeDecimal(r["Amount"])),
                    Category = (r.Table.Columns.Contains("CategoryName") ? (r["CategoryName"]?.ToString() ?? "(brak)") : "(brak)").Trim()
                }).ToList();

                var incomeTotal = incRows.Sum(x => x.Amount);
                var expenseTotal = expRows.Sum(x => x.Amount);

                var catDict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in incRows)
                {
                    var baseName = string.IsNullOrWhiteSpace(r.Category) ? "(brak)" : r.Category;
                    var name = $"Przychody: {baseName}";
                    catDict[name] = catDict.TryGetValue(name, out var cur) ? cur + r.Amount : r.Amount;
                }

                foreach (var r in expRows)
                {
                    var baseName = string.IsNullOrWhiteSpace(r.Category) ? "(brak)" : r.Category;
                    var name = $"Wydatki: {baseName}";
                    catDict[name] = catDict.TryGetValue(name, out var cur) ? cur + r.Amount : r.Amount;
                }

                var byCategory = catDict
                    .Select(kv => (Name: kv.Key, Sum: kv.Value))
                    .OrderByDescending(x => x.Sum)
                    .ToList();

                var merged = new List<(DateTime Date, decimal Amount)>();
                merged.AddRange(incRows.Select(x => (x.Date, x.Amount)));
                merged.AddRange(expRows.Select(x => (x.Date, -x.Amount)));

                var trend = BuildTrendList(merged, from, to, bucket, null);

                var byBankAccount = new List<(string Name, decimal Sum)>();
                var byFreeCash = new List<(string Name, decimal Sum)>();
                var bySavedCash = new List<(string Name, decimal Sum)>();
                var byEnvelope = new List<(string Name, decimal Sum)>();

                if (snapshot.Cash != 0m) byFreeCash.Add(("Wolna gotówka", Math.Abs(snapshot.Cash)));
                if (snapshot.Saved != 0m) bySavedCash.Add(("Odłożona gotówka", Math.Abs(snapshot.Saved)));
                if (snapshot.Envelopes != 0m) byEnvelope.Add(("Koperty", Math.Abs(snapshot.Envelopes)));

                var weekdays = GroupByWeekday(merged, null);
                var order = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };

                var byWeekday = order
                    .Select(d => (Name: PolishShortDayName(d), Sum: weekdays.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                var summaryNet = incomeTotal - expenseTotal;

                return (summaryNet, incomeTotal, expenseTotal,
                        byCategory, trend,
                        byBankAccount, byFreeCash, bySavedCash,
                        byEnvelope, byWeekday);
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

                var byCategory = rows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select(g => (Name: g.Key, Sum: g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Sum)
                    .ToList();

                var trend = BuildTrendList(
                    rows.Select(r => (r.Date, r.Amount)),
                    from, to, bucket, false);

                var byBank = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                var byFree = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                var bySaved = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in rows)
                {
                    var name = r.Source;
                    var amount = Math.Abs(r.Amount);

                    if (string.Equals(name, "Wolna gotówka", StringComparison.OrdinalIgnoreCase))
                        byFree[name] = byFree.TryGetValue(name, out var cur) ? cur + amount : amount;
                    else if (string.Equals(name, "Odłożona gotówka", StringComparison.OrdinalIgnoreCase))
                        bySaved[name] = bySaved.TryGetValue(name, out var cur) ? cur + amount : amount;
                    else if (name.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = name.Substring("Konto:".Length).Trim();
                        byBank[key] = byBank.TryGetValue(key, out var cur) ? cur + amount : amount;
                    }
                    else
                        byBank[name] = byBank.TryGetValue(name, out var cur) ? cur + amount : amount;
                }

                var byEnvelope = new List<(string Name, decimal Sum)>();

                var weekdaysDict = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), false);
                var orderInc = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };

                var byWeekday = orderInc
                    .Select(d => (Name: PolishShortDayName(d), Sum: weekdaysDict.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                if (byFree.Count == 0 && snapshot.Cash != 0m)
                    byFree["Wolna gotówka"] = Math.Abs(snapshot.Cash);

                if (bySaved.Count == 0 && snapshot.Saved != 0m)
                    bySaved["Odłożona gotówka"] = Math.Abs(snapshot.Saved);

                if (byEnvelope.Count == 0 && snapshot.Envelopes != 0m)
                    byEnvelope.Add(("Koperty", Math.Abs(snapshot.Envelopes)));

                return (total, total, 0m,
                        byCategory, trend,
                        byBank.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList(),
                        byFree.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList(),
                        bySaved.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList(),
                        byEnvelope, byWeekday);
            }

            if (SelectedMode == Mode.Transfer)
            {
                var dt = DatabaseService.GetTransfers(_userId, from, to);
                var rows = dt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = Math.Abs(SafeDecimal(r["Amount"])),
                    FromKind = (r["FromKind"]?.ToString() ?? "").Trim(),
                    ToKind = (r["ToKind"]?.ToString() ?? "").Trim(),
                }).ToList();

                var total = rows.Sum(x => x.Amount);

                string Human(string kind) => kind.ToLowerInvariant() switch
                {
                    "freecash" => "Wolna gotówka",
                    "savedcash" => "Odłożona gotówka",
                    "bank" => "Konto bankowe",
                    "envelope" => "Koperta",
                    _ => string.IsNullOrWhiteSpace(kind) ? "Inne" : kind
                };

                var byCategory = rows
                    .GroupBy(x => $"{Human(x.FromKind)} → {Human(x.ToKind)}")
                    .Select(g => (Name: g.Key, Sum: g.Sum(x => x.Amount)))
                    .OrderByDescending(x => x.Sum)
                    .ToList();

                var trend = BuildTrendList(
                    rows.Select(r => (r.Date, (decimal)r.Amount)),
                    from, to, bucket, false);

                var byAccount = new List<(string Name, decimal Sum)>();
                var byFreeCash = new List<(string Name, decimal Sum)>();
                var bySavedCash = new List<(string Name, decimal Sum)>();
                var byEnvelope = new List<(string Name, decimal Sum)>();

                var weekdaysDict = GroupByWeekday(rows.Select(r => (r.Date, (decimal)r.Amount)), false);
                var orderTr = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };

                var byWeekday = orderTr
                    .Select(d => (Name: PolishShortDayName(d), Sum: weekdaysDict.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                return (total, 0m, 0m,
                    byCategory, trend,
                    byAccount, byFreeCash, bySavedCash, byEnvelope, byWeekday);
            }

            // ===== EXPENSES =====
            {
                var dt = DatabaseService.GetExpenses(_userId, from, to);
                var rows = dt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Category = (r.Table.Columns.Contains("CategoryName") ? (r["CategoryName"]?.ToString() ?? "(brak)") : "(brak)").Trim(),
                    AccountText = (r.Table.Columns.Contains("Account") ? (r["Account"]?.ToString() ?? string.Empty) : string.Empty).Trim(),
                    AccountId = SafeNullableInt(r.Table.Columns.Contains("AccountId") ? r["AccountId"] : null)
                }).ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));

                var byCategory = rows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select(g => (Name: g.Key, Sum: g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Sum)
                    .ToList();

                var trend = BuildTrendList(rows.Select(r => (r.Date, r.Amount)), from, to, bucket, true);

                var byBank = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                var byFree = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                var bySaved = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                var byEnv = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                Dictionary<int, string>? accountsCache = null;

                foreach (var row in rows)
                {
                    var amount = Math.Abs(row.Amount);
                    var acc = row.AccountText ?? string.Empty;

                    if (acc.StartsWith("Koperta:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = acc.Substring("Koperta:".Length).Trim();
                        if (string.IsNullOrWhiteSpace(name)) name = "(bez nazwy)";
                        byEnv[name] = byEnv.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (acc.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = acc.Substring("Konto:".Length).Trim();
                        if (string.IsNullOrWhiteSpace(name)) name = "(konto)";
                        byBank[name] = byBank.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (string.Equals(acc, "Wolna gotówka", StringComparison.OrdinalIgnoreCase))
                    {
                        byFree["Wolna gotówka"] = byFree.TryGetValue("Wolna gotówka", out var cur) ? cur + amount : amount;
                    }
                    else if (string.Equals(acc, "Odłożona gotówka", StringComparison.OrdinalIgnoreCase))
                    {
                        bySaved["Odłożona gotówka"] = bySaved.TryGetValue("Odłożona gotówka", out var cur) ? cur + amount : amount;
                    }
                    else
                    {
                        string name;
                        if (row.AccountId is int id)
                        {
                            accountsCache ??= DatabaseService
                                .GetAccounts(_userId)
                                .ToDictionary(
                                    a => a.Id,
                                    a => string.IsNullOrWhiteSpace(a.AccountName)
                                        ? (a.BankName ?? $"Konto {a.Id}")
                                        : a.AccountName);

                            name = accountsCache.TryGetValue(id, out var n) ? n : $"Konto {id}";
                        }
                        else
                            name = string.IsNullOrWhiteSpace(acc) ? "Inne" : acc;

                        byBank[name] = byBank.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                }

                var weekdaysDict = GroupByWeekday(rows.Select(r => (r.Date, r.Amount)), true);
                var orderExp = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };

                var byWeekday = orderExp
                    .Select(d => (Name: PolishShortDayName(d), Sum: weekdaysDict.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                if (byFree.Count == 0 && snapshot.Cash != 0m)
                    byFree["Wolna gotówka"] = Math.Abs(snapshot.Cash);

                if (bySaved.Count == 0 && snapshot.Saved != 0m)
                    bySaved["Odłożona gotówka"] = Math.Abs(snapshot.Saved);

                if (byEnv.Count == 0 && snapshot.Envelopes != 0m)
                    byEnv["Koperty"] = Math.Abs(snapshot.Envelopes);

                return (total, 0m, total,
                        byCategory, trend,
                        byBank.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList(),
                        byFree.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList(),
                        bySaved.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList(),
                        byEnv.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList(),
                        byWeekday);
            }
        }

        // =========================
        // Trend + weekday
        // =========================

        private List<(string Label, decimal Value)> BuildTrendList(
            IEnumerable<(DateTime Date, decimal Amount)> items,
            DateTime? from, DateTime? to, string bucket, bool? isExpense)
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
                var cur = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
                while (cur <= end)
                {
                    var s = cur;
                    var e = cur.AddDays(6);
                    buckets.Add(($"{s:dd.MM}-{e:dd.MM}", s, e));
                    cur = cur.AddDays(7);
                }
            }
            else
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

                result.Add((b.Label, sum));
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
                dict[d] = dict.TryGetValue(d, out var cur) ? cur + amount : amount;
            }

            return dict;
        }

        private static string PolishShortDayName(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "Pn",
            DayOfWeek.Tuesday => "Wt",
            DayOfWeek.Wednesday => "Śr",
            DayOfWeek.Thursday => "Cz",
            DayOfWeek.Friday => "Pt",
            DayOfWeek.Saturday => "So",
            DayOfWeek.Sunday => "Nd",
            _ => d.ToString()
        };

        // =========================
        // Safe conversions
        // =========================

        private static DateTime SafeDate(object? o)
        {
            if (o == null || o is DBNull) return DateTime.Today;
            if (o is DateTime dt) return dt;

            return DateTime.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out var parsed)
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
                : (int?)null;
        }
    }
}
