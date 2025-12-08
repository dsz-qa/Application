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

        public ObservableCollection<ISeries> CategoriesSeries { get; } = new();
        public ObservableCollection<ISeries> TrendSeries { get; } = new();
        public ObservableCollection<ISeries> WeekdaySeries { get; } = new();
        public ObservableCollection<ISeries> EnvelopesSeries { get; } = new();
        public ObservableCollection<ISeries> BankAccountsSeries { get; } = new();
        public ObservableCollection<ISeries> FreeCashSeries { get; } = new();
        public ObservableCollection<ISeries> SavedCashSeries { get; } = new();
        // NEW: Amount buckets series
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
        // NEW: Amount buckets axes
        public ObservableCollection<Axis> AmountBucketsXAxes { get; } = new();
        public ObservableCollection<Axis> AmountBucketsYAxes { get; } = new();
        // NEW: Histogram axes for amount buckets
        public ObservableCollection<Axis> HistogramXAxes { get; } = new();
        public ObservableCollection<Axis> HistogramYAxes { get; } = new();

        public ObservableCollection<string> TrendLabels { get; } = new();
        public ObservableCollection<string> WeekdayLabels { get; } = new();
        public ObservableCollection<string> EnvelopeLabels { get; } = new();
        public ObservableCollection<string> BankAccountLabels { get; } = new();
        public ObservableCollection<string> FreeCashLabels { get; } = new();
        public ObservableCollection<string> SavedCashLabels { get; } = new();
        // NEW: Amount buckets labels
        public ObservableCollection<string> AmountBucketsLabels { get; } = new();

        private readonly int _userId;

        public ChartsViewModel()
        {
            _userId = UserService.GetCurrentUserId();
            try
            {
                DatabaseService.DataChanged += (_, __) => LoadStatistics();
            }
            catch { }

            LoadStatistics();
        }

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
            // clear histogram axes
            HistogramXAxes.Clear();
            HistogramYAxes.Clear();

            var (from, to, bucket) = ResolveRange();
            var white = new SolidColorPaint(SKColors.White);

            try
            {
                var data = BuildAggregates(from, to, bucket);

                // Pie: podzia³ wg kategorii
                if (data.ByCategory.Count == 0)
                {
                    CategoriesSeries.Add(new PieSeries<double>
                    {
                        Name = "Brak danych",
                        Values = new[] { 1d },
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
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
                            DataLabelsPaint = white,
                            DataLabelsFormatter = v => $"{v.Model:N2} z³"
                        });
                    }
                }

                // Trend
                if (data.Trend.Count == 0)
                {
                    TrendLabels.Add("-");
                    TrendSeries.Add(new LineSeries<double>
                    {
                        Values = new[] { 0d },
                        GeometrySize = 8,
                        Fill = null,
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
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
                        Fill = null,
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }

                TrendXAxes.Add(new Axis
                {
                    Labels = TrendLabels.ToArray(),
                    LabelsRotation = 45,
                    Name = "Okres",
                    LabelsPaint = white,
                    NamePaint = white
                });
                TrendYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Koperty
                if (data.ByEnvelope.Count == 0)
                {
                    EnvelopeLabels.Add("-");
                    EnvelopesSeries.Add(new ColumnSeries<double>
                    {
                        Values = new[] { 0d },
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }
                else
                {
                    foreach (var e in data.ByEnvelope)
                        EnvelopeLabels.Add(e.Name);

                    EnvelopesSeries.Add(new ColumnSeries<double>
                    {
                        Values = data.ByEnvelope.Select(e => (double)e.Sum).ToArray(),
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }

                EnvelopesXAxes.Add(new Axis
                {
                    Labels = EnvelopeLabels.ToArray(),
                    Name = "Koperty",
                    LabelsPaint = white,
                    NamePaint = white
                });
                EnvelopesYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Dni tygodnia
                double[] weekdayValues;

                if (data.ByWeekday.Count == 0)
                {
                    WeekdayLabels.Add("-");
                    weekdayValues = new[] { 0d };
                    WeekdaySeries.Add(new ColumnSeries<double>
                    {
                        Values = weekdayValues,
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }
                else
                {
                    foreach (var d in data.ByWeekday)
                        WeekdayLabels.Add(d.Name);

                    weekdayValues = data.ByWeekday
                        .Select(d => (double)d.Sum)
                        .ToArray();

                    WeekdaySeries.Add(new ColumnSeries<double>
                    {
                        Values = weekdayValues,
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }

                WeekdayXAxes.Add(new Axis
                {
                    Labels = WeekdayLabels.ToArray(),
                    Name = "Dzieñ tygodnia",
                    LabelsPaint = white,
                    NamePaint = white
                });

                // oœ Y tak, ¿eby wyraŸnie by³o widaæ 0 i s³upki nad / pod nim
                var minWeekday = weekdayValues.Min();
                var maxWeekday = weekdayValues.Max();

                var minLimit = Math.Min(0, minWeekday);
                var maxLimit = Math.Max(0, maxWeekday);

                // jeœli wszystkie wartoœci s¹ takie same, trochê rozci¹gamy zakres
                if (Math.Abs(maxLimit - minLimit) < 0.01)
                {
                    if (maxLimit == 0)
                    {
                        minLimit = -1;
                        maxLimit = 1;
                    }
                    else
                    {
                        var margin = Math.Abs(maxLimit) * 0.1; // use double
                        minLimit = minLimit - margin;
                        maxLimit = maxLimit + margin;
                    }
                }

                WeekdayYAxes.Add(new Axis
                {
                    MinLimit = minLimit,
                    MaxLimit = maxLimit,
                    LabelsPaint = white,
                    NamePaint = white
                });

                // Rachunki bankowe
                if (data.ByBankAccount.Count == 0)
                {
                    BankAccountLabels.Add("-");
                    BankAccountsSeries.Add(new ColumnSeries<double>
                    {
                        Values = new[] { 0d },
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }
                else
                {
                    foreach (var a in data.ByBankAccount)
                        BankAccountLabels.Add(a.Name);

                    BankAccountsSeries.Add(new ColumnSeries<double>
                    {
                        Values = data.ByBankAccount.Select(a => (double)a.Sum).ToArray(),
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }

                BankAccountsXAxes.Add(new Axis
                {
                    Labels = BankAccountLabels.ToArray(),
                    Name = "Rachunki bankowe",
                    LabelsPaint = white,
                    NamePaint = white
                });
                BankAccountsYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Wolna gotówka
                if (data.FreeCash.Count == 0)
                {
                    FreeCashLabels.Add("-");
                    FreeCashSeries.Add(new ColumnSeries<double>
                    {
                        Values = new[] { 0d },
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }
                else
                {
                    foreach (var a in data.FreeCash)
                        FreeCashLabels.Add(a.Name);

                    FreeCashSeries.Add(new ColumnSeries<double>
                    {
                        Values = data.FreeCash.Select(a => (double)a.Sum).ToArray(),
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }

                FreeCashXAxes.Add(new Axis
                {
                    Labels = FreeCashLabels.ToArray(),
                    Name = "Wolna gotówka",
                    LabelsPaint = white,
                    NamePaint = white
                });
                FreeCashYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // Od³o¿ona gotówka
                if (data.SavedCash.Count == 0)
                {
                    SavedCashLabels.Add("-");
                    SavedCashSeries.Add(new ColumnSeries<double>
                    {
                        Values = new[] { 0d },
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }
                else
                {
                    foreach (var a in data.SavedCash)
                        SavedCashLabels.Add(a.Name);

                    SavedCashSeries.Add(new ColumnSeries<double>
                    {
                        Values = data.SavedCash.Select(a => (double)a.Sum).ToArray(),
                        DataLabelsPaint = white,
                        DataLabelsFormatter = v => $"{v.Model:N2} z³"
                    });
                }

                SavedCashXAxes.Add(new Axis
                {
                    Labels = SavedCashLabels.ToArray(),
                    Name = "Od³o¿ona gotówka",
                    LabelsPaint = white,
                    NamePaint = white
                });
                SavedCashYAxes.Add(new Axis { LabelsPaint = white, NamePaint = white });

                // NEW: Build amount buckets chart at the end
                BuildAmountBucketsChart(from, to);
            }
            catch
            {
                var whitePaint = new SolidColorPaint(SKColors.White);

                CategoriesSeries.Add(new PieSeries<double>
                {
                    Name = "Brak danych",
                    Values = new[] { 1d },
                    DataLabelsPaint = whitePaint,
                    DataLabelsFormatter = v => $"{v.Model:N2} z³"
                });

                TrendLabels.Add("-");
                TrendSeries.Add(new LineSeries<double>
                {
                    Values = new[] { 0d },
                    Fill = null,
                    DataLabelsPaint = whitePaint,
                    DataLabelsFormatter = v => $"{v.Model:N2} z³"
                });
                TrendXAxes.Add(new Axis
                {
                    Labels = TrendLabels.ToArray(),
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint
                });
                TrendYAxes.Add(new Axis { LabelsPaint = whitePaint, NamePaint = whitePaint });

                EnvelopeLabels.Add("-");
                EnvelopesSeries.Add(new ColumnSeries<double>
                {
                    Values = new[] { 0d },
                    DataLabelsPaint = whitePaint,
                    DataLabelsFormatter = v => $"{v.Model:N2} z³"
                });
                EnvelopesXAxes.Add(new Axis
                {
                    Labels = EnvelopeLabels.ToArray(),
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint
                });
                EnvelopesYAxes.Add(new Axis { LabelsPaint = whitePaint, NamePaint = whitePaint });

                BankAccountLabels.Add("-");
                BankAccountsSeries.Add(new ColumnSeries<double>
                {
                    Values = new[] { 0d },
                    DataLabelsPaint = whitePaint,
                    DataLabelsFormatter = v => $"{v.Model:N2} z³"
                });
                BankAccountsXAxes.Add(new Axis
                {
                    Labels = BankAccountLabels.ToArray(),
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint
                });
                BankAccountsYAxes.Add(new Axis { LabelsPaint = whitePaint, NamePaint = whitePaint });

                FreeCashLabels.Add("-");
                FreeCashSeries.Add(new ColumnSeries<double>
                {
                    Values = new[] { 0d },
                    DataLabelsPaint = whitePaint,
                    DataLabelsFormatter = v => $"{v.Model:N2} z³"
                });
                FreeCashXAxes.Add(new Axis
                {
                    Labels = FreeCashLabels.ToArray(),
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint
                });
                FreeCashYAxes.Add(new Axis { LabelsPaint = whitePaint, NamePaint = whitePaint });

                SavedCashLabels.Add("-");
                SavedCashSeries.Add(new ColumnSeries<double>
                {
                    Values = new[] { 0d },
                    DataLabelsPaint = whitePaint,
                    DataLabelsFormatter = v => $"{v.Model:N2} z³"
                });
                SavedCashXAxes.Add(new Axis
                {
                    Labels = SavedCashLabels.ToArray(),
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint
                });
                SavedCashYAxes.Add(new Axis { LabelsPaint = whitePaint, NamePaint = whitePaint });

                WeekdayLabels.Add("-");
                WeekdaySeries.Add(new ColumnSeries<double>
                {
                    Values = new[] { 0d },
                    DataLabelsPaint = whitePaint,
                    DataLabelsFormatter = v => $"{v.Model:N2} z³"
                });
                WeekdayXAxes.Add(new Axis
                {
                    Labels = WeekdayLabels.ToArray(),
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint
                });
                WeekdayYAxes.Add(new Axis { LabelsPaint = whitePaint, NamePaint = whitePaint });

                // NEW: Empty amount buckets on error
                AmountBucketsLabels.Add("-");
                AmountBucketsSeries.Add(new ColumnSeries<double>
                {
                    Values = new[] { 0d },
                    DataLabelsPaint = whitePaint
                });
                AmountBucketsXAxes.Add(new Axis
                {
                    Labels = AmountBucketsLabels.ToArray(),
                    Name = "Przedzia³ kwot [PLN]",
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint
                });

                AmountBucketsYAxes.Add(new Axis
                {
                    Name = "Liczba transakcji",
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint,
                    // nie pokazuj etykiety "0", ¿eby nie klei³a siê do pierwszego przedzia³u
                    Labeler = value => value <= 0 ? string.Empty : value.ToString("0")
                });

                // NEW: Empty histogram axes on error
                HistogramXAxes.Clear();
                HistogramYAxes.Clear();
                HistogramXAxes.Add(new Axis
                {
                    Labels = AmountBucketsLabels.ToArray(),
                    Name = "Przedzia³ kwot [PLN]",
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint,
                    LabelsRotation = 0,
                    SeparatorsPaint = whitePaint,
                    TextSize = 14,
                    Padding = new LiveChartsCore.Drawing.Padding(0, 16, 0, 0)
                });
                HistogramYAxes.Add(new Axis
                {
                    LabelsPaint = whitePaint,
                    NamePaint = whitePaint,
                    MinLimit = -0.5,
                    MaxLimit = 1
                });
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

                            // Bankowe / wolna / od³o¿ona
                            col.Item().PaddingTop(10).Text("Rachunki bankowe").Bold();
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
                                foreach (var r2 in data.ByBankAccount)
                                {
                                    t.Cell().Text(r2.Name);
                                    t.Cell().Text(r2.Sum.ToString("N2"));
                                }
                            });

                            col.Item().PaddingTop(10).Text("Wolna gotówka").Bold();
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
                                foreach (var r2 in data.FreeCash)
                                {
                                    t.Cell().Text(r2.Name);
                                    t.Cell().Text(r2.Sum.ToString("N2"));
                                }
                            });

                            col.Item().PaddingTop(10).Text("Od³o¿ona gotówka").Bold();
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
                                foreach (var r2 in data.SavedCash)
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
                w.WriteLine("Rachunki bankowe");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByBankAccount)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Wolna gotówka");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.FreeCash)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Od³o¿ona gotówka");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.SavedCash)
                    w.WriteLine($"{Escape(r.Name)};{r.Sum.ToString("N2", CultureInfo.InvariantCulture)}");

                w.WriteLine();
                w.WriteLine("Koperty");
                w.WriteLine("Nazwa;Suma");
                foreach (var r in data.ByEnvelope)
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
            // aktualny snapshot œrodków – fallback dla gotówki i kopert
            var snapshot = DatabaseService.GetMoneySnapshot(_userId);

            // ===== CASHFLOW =====
            if (SelectedMode == Mode.Cashflow)
            {
                // wydatki z kategoriami
                var expDt = DatabaseService.GetExpenses(_userId, from, to);
                var expRows = expDt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Category = (r.Table.Columns.Contains("CategoryName")
                        ? (r["CategoryName"]?.ToString() ?? "(brak)")
                        : "(brak)").Trim()
                }).ToList();

                // przychody z kategoriami
                var incDt = DatabaseService.GetIncomes(_userId, from, to);
                var incRows = incDt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Category = (r.Table.Columns.Contains("CategoryName")
                        ? (r["CategoryName"]?.ToString() ?? "(brak)")
                        : "(brak)").Trim()
                }).ToList();

                var incomeTotal = incRows.Sum(x => Math.Abs(x.Amount));
                var expenseTotal = expRows.Sum(x => Math.Abs(x.Amount));

                // ===== PODZIA£ WG KATEGORII DLA CASHFLOW =====
                // Rozbijamy osobno przychody i wydatki po kategoriach:
                // "Przychody: Pensja", "Wydatki: Jedzenie" itd.
                var catDict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in incRows)
                {
                    var baseName = string.IsNullOrWhiteSpace(r.Category) ? "(brak)" : r.Category;
                    var name = $"Przychody: {baseName}";
                    var amount = Math.Abs(r.Amount);
                    catDict[name] = catDict.TryGetValue(name, out var cur) ? cur + amount : amount;
                }

                foreach (var r in expRows)
                {
                    var baseName = string.IsNullOrWhiteSpace(r.Category) ? "(brak)" : r.Category;
                    var name = $"Wydatki: {baseName}";
                    var amount = Math.Abs(r.Amount);
                    catDict[name] = catDict.TryGetValue(name, out var cur) ? cur + amount : amount;
                }

                var byCategory = catDict
                    .Select(kv => (kv.Key, kv.Value))
                    .OrderByDescending(x => x.Value)
                    .ToList();

                // ===== TREND / DNI TYGODNIA – jak wczeœniej =====
                var merged = new List<(DateTime Date, decimal Amount)>();
                merged.AddRange(incRows.Select(x => (x.Date, Math.Abs(x.Amount))));
                merged.AddRange(expRows.Select(x => (x.Date, -Math.Abs(x.Amount))));

                var trend = BuildTrendList(merged, from, to, bucket, null);

                // Konta – zostawiamy prosty widok: ca³oœæ przychodów vs wydatków
                var byAccount = new List<(string Name, decimal Sum)>
                {
                    ("Przychody", incomeTotal),
                    ("Wydatki", expenseTotal)
                };

                var byEnvelope = new List<(string Name, decimal Sum)>(); // cashflow: brak sensownego rozbicia na koperty

                var weekdays = GroupByWeekday(merged, null);
                var order = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = order
                    .Select(d => (PolishShortDayName(d), weekdays.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                var byFreeCash = new List<(string Name, decimal Sum)>();
                var bySavedCash = new List<(string Name, decimal Sum)>();

                var summary = incomeTotal - expenseTotal;

                return (summary, incomeTotal, expenseTotal,
                        byCategory, trend,
                        byAccount, byFreeCash, bySavedCash,
                        byEnvelope, byWeekday);
            }
            // ===== INCOMES =====
            if (SelectedMode == Mode.Incomes)
            {
                var dt = DatabaseService.GetIncomes(_userId, from, to);
                var rows = dt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Category = (r.Table.Columns.Contains("CategoryName")
                        ? (r["CategoryName"]?.ToString() ?? "(brak)")
                        : "(brak)").Trim(),
                    Source = (r.Table.Columns.Contains("Source")
                        ? (r["Source"]?.ToString() ?? "Przychody")
                        : "Przychody").Trim()
                }).ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));

                var byCategory = rows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
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
                    {
                        byFree[name] = byFree.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (string.Equals(name, "Od³o¿ona gotówka", StringComparison.OrdinalIgnoreCase))
                    {
                        bySaved[name] = bySaved.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (name.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = name.Substring("Konto:".Length).Trim();
                        byBank[key] = byBank.TryGetValue(key, out var cur) ? cur + amount : amount;
                    }
                    else
                    {
                        byBank[name] = byBank.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                }

                var byEnvelope = new List<(string, decimal)>();

                var weekdaysDict = GroupByWeekday(
                    rows.Select(r => (r.Date, r.Amount)), false);

                var orderInc = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = orderInc
                    .Select(d => (PolishShortDayName(d),
                                  weekdaysDict.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                // fallback z snapshotu
                if (byFree.Count == 0 && snapshot.Cash != 0m)
                    byFree["Wolna gotówka"] = Math.Abs(snapshot.Cash);

                if (bySaved.Count == 0 && snapshot.Saved != 0m)
                    bySaved["Od³o¿ona gotówka"] = Math.Abs(snapshot.Saved);

                if (byEnvelope.Count == 0 && snapshot.Envelopes != 0m)
                    byEnvelope.Add(("Koperty", Math.Abs(snapshot.Envelopes)));

                return (total, total, 0m,
                        byCategory, trend,
                        byBank.Select(kv => (kv.Key, kv.Value))
                              .OrderByDescending(x => x.Value).ToList(),
                        byFree.Select(kv => (kv.Key, kv.Value))
                              .OrderByDescending(x => x.Value).ToList(),
                        bySaved.Select(kv => (kv.Key, kv.Value))
                               .OrderByDescending(x => x.Value).ToList(),
                        byEnvelope, byWeekday);
            }

            // ===== TRANSFER =====
            if (SelectedMode == Mode.Transfer)
            {
                var dt = DatabaseService.GetIncomes(_userId, from, to);
                var rows = dt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Source = (r.Table.Columns.Contains("Source")
                        ? (r["Source"]?.ToString() ?? string.Empty)
                        : string.Empty).Trim()
                })
                .Where(x => string.Equals(x.Source, "Przelew", StringComparison.OrdinalIgnoreCase)
                            || x.Source.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                .ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));

                var byCategory = new List<(string, decimal)> { ("Transfery", total) };

                var trend = BuildTrendList(
                    rows.Select(r => (r.Date, r.Amount)),
                    from, to, bucket, false);

                var byAccount = rows
                    .GroupBy(r => r.Source)
                    .Select(g => (string.IsNullOrWhiteSpace(g.Key) ? "Przelew" : g.Key,
                                  g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
                    .ToList();

                var byEnvelope = new List<(string, decimal)>();

                var weekdaysDict = GroupByWeekday(
                    rows.Select(r => (r.Date, r.Amount)), false);

                var orderTr = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = orderTr
                    .Select(d => (PolishShortDayName(d),
                                  weekdaysDict.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                var byFreeCash = new List<(string Name, decimal Sum)>();
                var bySavedCash = new List<(string Name, decimal Sum)>();

                if (snapshot.Cash != 0m)
                    byFreeCash.Add(("Wolna gotówka", Math.Abs(snapshot.Cash)));

                if (snapshot.Saved != 0m)
                    bySavedCash.Add(("Od³o¿ona gotówka", Math.Abs(snapshot.Saved)));

                if (snapshot.Envelopes != 0m)
                    byEnvelope.Add(("Koperty", Math.Abs(snapshot.Envelopes)));

                return (total, 0m, 0m,
                        byCategory, trend,
                        byAccount, byFreeCash, bySavedCash, byEnvelope, byWeekday);
            }

            // ===== EXPENSES (domyœlnie) =====
            {
                var dt = DatabaseService.GetExpenses(_userId, from, to);
                var rows = dt.AsEnumerable().Select(r => new
                {
                    Date = SafeDate(r["Date"]),
                    Amount = SafeDecimal(r["Amount"]),
                    Category = (r.Table.Columns.Contains("CategoryName")
                        ? (r["CategoryName"]?.ToString() ?? "(brak)")
                        : "(brak)").Trim(),
                    AccountText = (r.Table.Columns.Contains("Account")
                        ? (r["Account"]?.ToString() ?? string.Empty)
                        : string.Empty).Trim(),
                    AccountId = SafeNullableInt(r["AccountId"])
                }).ToList();

                var total = rows.Sum(x => Math.Abs(x.Amount));

                var byCategory = rows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "(brak)" : x.Category)
                    .Select(g => (g.Key, g.Sum(x => Math.Abs(x.Amount))))
                    .OrderByDescending(x => x.Item2)
                    .ToList();

                var trend = BuildTrendList(
                    rows.Select(r => (r.Date, r.Amount)),
                    from, to, bucket, true);

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
                        const string name = "Wolna gotówka";
                        byFree[name] = byFree.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                    else if (string.Equals(acc, "Od³o¿ona gotówka", StringComparison.OrdinalIgnoreCase))
                    {
                        const string name = "Od³o¿ona gotówka";
                        bySaved[name] = bySaved.TryGetValue(name, out var cur) ? cur + amount : amount;
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

                            name = accountsCache.TryGetValue(id, out var n)
                                ? n
                                : $"Konto {id}";
                        }
                        else
                        {
                            name = string.IsNullOrWhiteSpace(acc) ? "Inne" : acc;
                        }

                        byBank[name] = byBank.TryGetValue(name, out var cur) ? cur + amount : amount;
                    }
                }

                var weekdaysDict = GroupByWeekday(
                    rows.Select(r => (r.Date, r.Amount)), true);

                var orderExp = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                };
                var byWeekday = orderExp
                    .Select(d => (PolishShortDayName(d),
                                  weekdaysDict.TryGetValue(d, out var v) ? v : 0m))
                    .ToList();

                // fallback z snapshotu, gdy brak danych
                if (byFree.Count == 0 && snapshot.Cash != 0m)
                    byFree["Wolna gotówka"] = Math.Abs(snapshot.Cash);

                if (bySaved.Count == 0 && snapshot.Saved != 0m)
                    bySaved["Od³o¿ona gotówka"] = Math.Abs(snapshot.Saved);

                if (byEnv.Count == 0 && snapshot.Envelopes != 0m)
                    byEnv["Koperty"] = Math.Abs(snapshot.Envelopes);

                return (total, 0m, total,
                        byCategory, trend,
                        byBank.Select(kv => (kv.Key, kv.Value))
                              .OrderByDescending(x => x.Value).ToList(),
                        byFree.Select(kv => (kv.Key, kv.Value))
                              .OrderByDescending(x => x.Value).ToList(),
                        bySaved.Select(kv => (kv.Key, kv.Value))
                               .OrderByDescending(x => x.Value).ToList(),
                        byEnv.Select(kv => (kv.Key, kv.Value))
                             .OrderByDescending(x => x.Value).ToList(),
                        byWeekday);
            }
        }

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

            return DateTime.TryParse(
                       Convert.ToString(o, CultureInfo.InvariantCulture),
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

            return int.TryParse(
                       Convert.ToString(o, CultureInfo.InvariantCulture),
                       out var parsed)
                ? parsed
                : (int?)null;
        }

        // ===== NEW: Amount buckets =====
        private void BuildAmountBucketsChart(DateTime? from, DateTime? to)
        {
            var white = new SolidColorPaint(SKColors.White);
            var bucketsData = GetAmountBucketsData(from, to);

            if (bucketsData.Count == 0 || bucketsData.All(b => b.Sum == 0))
            {
                AmountBucketsLabels.Add("-");
                AmountBucketsSeries.Add(new ColumnSeries<double>
                {
                    Values = new[] { 0d },
                    DataLabelsPaint = white
                });
            }
            else
            {
                foreach (var b in bucketsData)
                    AmountBucketsLabels.Add(b.Name);

                AmountBucketsSeries.Add(new ColumnSeries<double>
                {
                    Values = bucketsData.Select(b => (double)b.Sum).ToArray(),
                    DataLabelsPaint = white
                });
            }

            // Keep legacy axes for compatibility
            AmountBucketsXAxes.Add(new Axis
            {
                Labels = AmountBucketsLabels.ToArray(),
                Name = "Przedzia³ kwot [PLN]",
                LabelsPaint = white,
                NamePaint = white
            });

            AmountBucketsYAxes.Add(new Axis
            {
                Name = "Liczba transakcji",
                LabelsPaint = white,
                NamePaint = white,
                Labeler = value => value <= 0 ? string.Empty : value.ToString("0")
            });

            // NEW: Histogram axes with padding and adjusted zero line
            var labelsArray = AmountBucketsLabels.ToArray();
            HistogramXAxes.Add(new Axis
            {
                Labels = labelsArray,
                LabelsPaint = white,
                Name = "Przedzia³ kwot [PLN]",
                NamePaint = white,
                LabelsRotation = 0,
                SeparatorsPaint = white,
                TextSize = 14,
                Padding = new LiveChartsCore.Drawing.Padding(0, 16, 0, 0)
            });

            var maxValue = bucketsData.Count == 0 ? 0d : (double)bucketsData.Max(b => b.Sum);
            HistogramYAxes.Add(new Axis
            {
                LabelsPaint = white,
                NamePaint = white,
                MinLimit = -0.5,
                MaxLimit = maxValue + 1
            });
        }

        private List<(string Name, decimal Sum)> GetAmountBucketsData(DateTime? from, DateTime? to)
        {
            var amounts = new List<decimal>();
            if (SelectedMode == Mode.Cashflow)
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
                    .Select(r => new { Amount = Math.Abs(SafeDecimal(r["Amount"])), Source = (r["Source"]?.ToString() ?? "").Trim() });
                if (SelectedMode == Mode.Transfer)
                {
                    amounts.AddRange(inc
                        .Where(x => x.Source.Equals("Przelew", StringComparison.OrdinalIgnoreCase)
                                 || x.Source.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Amount));
                }
                else
                {
                    amounts.AddRange(inc.Select(x => x.Amount));
                }
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

                result[label] = result.ContainsKey(label) ? result[label] + 1 : 1;
            }

            var ordered = new[] { "0–50", "50–100", "100–200", "200–500", "500–1000", ">1000" };
            return ordered.Select(l => (l, result.ContainsKey(l) ? result[l] : 0m)).ToList();
        }
    }
}
