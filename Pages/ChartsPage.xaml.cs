using Finly.Models;
using Finly.Services;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Finly.Pages
{
    public partial class ChartsPage : UserControl
    {
        private readonly int _userId;
        private List<Expense> _expenses = new();

        public ChartsPage() : this(SafeGetUserId()) { }

        public ChartsPage(int userId)
        {
            InitializeComponent();
            _userId = userId;
            LoadChartData(); // startowo bez filtrów
        }

        private static int SafeGetUserId()
        {
            try { return UserService.GetCurrentUserId(); }
            catch { return 0; }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            var start = FromDatePicker.SelectedDate;
            var end = ToDatePicker.SelectedDate;
            LoadChartData(start, end);
        }

        private void LoadChartData(DateTime? start = null, DateTime? end = null)
        {
            var all = DatabaseService.GetExpensesWithCategory()
                .Where(e => e.UserId == _userId)
                .ToList();

            var filtered = all
                .Where(e =>
                    (!start.HasValue || e.Date >= start.Value) &&
                    (!end.HasValue || e.Date <= end.Value))
                .ToList();

            LoadPieChart(filtered);
            LoadLineChart(filtered);

            _expenses = filtered.Select(e => new Expense
            {
                Id = e.Id,
                Amount = e.Amount,
                Category = e.CategoryName,
                Date = e.Date,
                Description = e.Description ?? string.Empty
            }).ToList();
        }

        // ============== PIE ==============
        private void LoadPieChart(List<ExpenseDisplayModel> data)
        {
            var grouped = data
                .GroupBy(e => string.IsNullOrWhiteSpace(e.CategoryName) ? "Brak kategorii" : e.CategoryName)
                .Select(g =>
                {
                    var sum = g.Sum(x => x.Amount);
                    return new PieSeries<double>
                    {
                        Name = g.Key,
                        Values = new[] { sum },
                        // etykiety prosto – bez odwołań do PrimaryValue (różnice między wersjami)
                        DataLabelsSize = 12,
                        DataLabelsPaint = new SolidColorPaint(SKColors.White),
                        DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle
                    };
                })
                .ToArray();

            pieChart.Series = grouped;
        }

        // ============== LINE ==============
        private void LoadLineChart(List<ExpenseDisplayModel> data)
        {
            var points = data
                .GroupBy(e => e.Date.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Date = g.Key,
                    Amount = g.Sum(e => e.Amount)
                })
                .ToList();

            lineChart.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = points.Select(p => p.Amount).ToArray(),
                    Fill = null,
                    GeometrySize = 8
                }
            };

            lineChart.XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = points.Select(p => p.Date.ToString("dd.MM.yyyy")).ToArray(),
                    LabelsRotation = 45,
                    Name = "Data",
                    LabelsPaint = new SolidColorPaint(SKColors.Gray)
                }
            };

            lineChart.YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Kwota [zł]",
                    LabelsPaint = new SolidColorPaint(SKColors.Gray),
                    // Jeśli wolisz pełne wartości, usuń Labeler:
                    Labeler = value => value >= 1000 ? $"{value/1000:0.#} tys." : $"{value:0}"
                }
            };
        }

        // ============== EXPORT PNG ==============
        private void ExportChartsToPng_Click(object sender, RoutedEventArgs e)
        {
            // Pie
            var dlgPie = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                FileName = "WykresKolowy"
            };
            if (dlgPie.ShowDialog() != true) return;

            // Line
            var dlgLine = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                FileName = "WykresLiniowy"
            };
            if (dlgLine.ShowDialog() != true) return;

            SaveVisualAsPng(pieChart, dlgPie.FileName);
            SaveVisualAsPng(lineChart, dlgLine.FileName);

            MessageBox.Show("Wykresy zapisano jako PNG.", "Sukces",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void SaveVisualAsPng(FrameworkElement visual, string path)
        {
            var bmp = new RenderTargetBitmap(
                Math.Max(1, (int)visual.ActualWidth),
                Math.Max(1, (int)visual.ActualHeight),
                96, 96, PixelFormats.Pbgra32);

            bmp.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        // ============== EXPORT PDF (QuestPDF) ==============
        private void ExportChartToPdf_Click(object sender, RoutedEventArgs e)
        {
            // tymczasowe PNG
            var pieTemp = Path.Combine(Path.GetTempPath(), $"finly_pie_{Guid.NewGuid():N}.png");
            var lineTemp = Path.Combine(Path.GetTempPath(), $"finly_line_{Guid.NewGuid():N}.png");

            SaveVisualAsPng(pieChart, pieTemp);
            SaveVisualAsPng(lineChart, lineTemp);

            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = "wykresy_wydatkow"
                };
                if (sfd.ShowDialog() != true) return;

                // QuestPDF – jedna strona, dwa obrazy pod sobą
                var pieBytes = File.ReadAllBytes(pieTemp);
                var lineBytes = File.ReadAllBytes(lineTemp);

                var doc = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(20);
                        page.DefaultTextStyle(ts => ts.FontSize(12));

                        page.Content().Column(col =>
                        {
                            col.Spacing(10);
                            col.Item().Text("Wykresy wydatków").FontSize(16).SemiBold().AlignCenter();
                            col.Item().Image(pieBytes);
                            col.Item().Image(lineBytes);
                        });
                    });
                });

                doc.GeneratePdf(sfd.FileName);

                MessageBox.Show("Wykresy zapisane do PDF.", "Sukces",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // sprzątanie
                try { File.Delete(pieTemp); } catch { }
                try { File.Delete(lineTemp); } catch { }
            }
        }

        // ============== SORTOWANIE / ZDARZENIA UI ==============
        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (SortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (_expenses == null || _expenses.Count == 0)
            {
                MessageBox.Show("Brak danych do sortowania.", "Błąd",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sorted = _expenses;

            switch (selected)
            {
                case "Kategoria A-Z": sorted = _expenses.OrderBy(x => x.Category).ToList(); break;
                case "Kategoria Z-A": sorted = _expenses.OrderByDescending(x => x.Category).ToList(); break;
                case "Suma rosnąco": sorted = _expenses.OrderBy(x => x.Amount).ToList(); break;
                case "Suma malejąco": sorted = _expenses.OrderByDescending(x => x.Amount).ToList(); break;
            }

            var mapped = sorted.Select(x => new ExpenseDisplayModel
            {
                Id = x.Id,
                Amount = x.Amount,
                CategoryName = x.Category,
                Date = x.Date,
                Description = x.Description
            }).ToList();

            LoadPieChart(mapped);
        }

        private void DateSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (DateSortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (_expenses == null || _expenses.Count == 0)
            {
                MessageBox.Show("Brak danych do sortowania.", "Błąd",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sorted = _expenses;

            switch (selected)
            {
                case "Data rosnąco": sorted = _expenses.OrderBy(x => x.Date).ToList(); break;
                case "Data malejąco": sorted = _expenses.OrderByDescending(x => x.Date).ToList(); break;
            }

            var mapped = sorted.Select(x => new ExpenseDisplayModel
            {
                Amount = x.Amount,
                Date = x.Date,
                CategoryName = x.Category,
                Description = x.Description
            }).ToList();

            LoadLineChart(mapped);
        }
    }
}

