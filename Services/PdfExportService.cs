using System;
using System.IO;
using System.Linq;
using Finly.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Finly.Services
{
    /// <summary>
    /// Serwis odpowiedzialny za eksport pełnego raportu finansowego do PDF
    /// z wykorzystaniem biblioteki QuestPDF.
    /// </summary>
    public static class PdfExportService
    {
        /// <summary>
        /// Generuje raport PDF na podstawie bieżącego stanu ReportsViewModel.
        /// Zwraca pełną ścieżkę do wygenerowanego pliku.
        /// </summary>
        public static string ExportReportsPdf(ReportsViewModel vm)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var fileName = ReportsService.BuildDefaultReportFileName(vm.FromDate, vm.ToDate);
            var path = Path.Combine(desktop, fileName);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header()
                        .Text("Finly – raport finansowy")
                        .SemiBold()
                        .FontSize(18);

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Text($"Okres: {vm.FromDate:dd.MM.yyyy} – {vm.ToDate:dd.MM.yyyy}");
                        if (!string.IsNullOrWhiteSpace(vm.ComparisonPeriodLabel))
                            col.Item().Text(vm.ComparisonPeriodLabel);

                        col.Item().Element(c => KpiSection(c, vm));
                        col.Item().Element(c => CategoriesSection(c, vm));
                        col.Item().Element(c => TransactionsSection(c, vm));
                        col.Item().Element(c => InsightsSection(c, vm));
                    });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Finly • ");
                            x.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
                        });
                });
            });

            document.GeneratePdf(path);
            return path;
        }

        // ===== Sekcje dokumentu =====

        private static void KpiSection(IContainer container, ReportsViewModel vm)
        {
            container.Column(col =>
            {
                col.Item().Text("Podsumowanie KPI")
                    .Bold()
                    .FontSize(13);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(3);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Nazwa");

                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Wartość");
                    });

                    foreach (var kv in vm.KPIList)
                    {
                        table.Cell().Element(c => c.Padding(4)).Text(kv.Key);
                        table.Cell().Element(c => c.Padding(4)).Text(kv.Value);
                    }
                });
            });
        }

        private static void CategoriesSection(IContainer container, ReportsViewModel vm)
        {
            container.Column(col =>
            {
                col.Item().Text("Kategorie wydatków (ten okres)")
                    .Bold()
                    .FontSize(13);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Kategoria");

                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Kwota");

                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Udział %");
                    });

                    foreach (var row in vm.Details)
                    {
                        table.Cell().Element(c => c.Padding(4)).Text(row.Name);
                        table.Cell().Element(c => c.Padding(4)).Text(row.Amount.ToString("N2"));
                        table.Cell().Element(c => c.Padding(4)).Text(row.SharePercent.ToString("N1"));
                    }
                });
            });
        }

        private static void TransactionsSection(IContainer container, ReportsViewModel vm)
        {
            container.Column(col =>
            {
                col.Item().Text("Transakcje (ten okres)")
                    .Bold()
                    .FontSize(13);

                var transactions = vm.FilteredTransactions
                    .OrderByDescending(t => t.Date)
                    .ToList();

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2); // data
                        cols.RelativeColumn(3); // kategoria
                        cols.RelativeColumn(5); // opis
                        cols.RelativeColumn(2); // kwota
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Data");

                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Kategoria");

                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Opis");

                        header.Cell().Element(c =>
                            c.DefaultTextStyle(x => x.SemiBold())
                             .Padding(4)
                             .Background(Colors.Grey.Lighten3))
                             .Text("Kwota");
                    });

                    foreach (var t in transactions)
                    {
                        table.Cell().Element(c => c.Padding(4)).Text(t.Date.ToString("dd.MM.yyyy"));
                        table.Cell().Element(c => c.Padding(4)).Text(t.Category);
                        table.Cell().Element(c => c.Padding(4)).Text(t.Description);
                        table.Cell().Element(c => c.Padding(4)).Text(t.Amount.ToString("N2"));
                    }
                });
            });
        }

        private static void InsightsSection(IContainer container, ReportsViewModel vm)
        {
            if (vm.Insights == null || vm.Insights.Count == 0)
                return;

            container.Column(col =>
            {
                col.Item().Text("Wnioski finansowe")
                    .Bold()
                    .FontSize(13);

                foreach (var insight in vm.Insights)
                {
                    col.Item().Text("• " + insight);
                }
            });
        }
    }
}
