using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Finly.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Finly.Services.SpecificPages;

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
            => ExportReportsPdf(vm, null);

        /// <summary>
        /// donutChartPng – obraz PNG z samego wykresu (donut). Legenda jest generowana po prawej w PDF.
        /// </summary>
        public static string ExportReportsPdf(ReportsViewModel vm, byte[]? donutChartPng)
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

                        // ===== WYKRES + LEGENDA =====
                        if (donutChartPng != null && donutChartPng.Length > 0)
                        {
                            col.Item().PaddingTop(6)
                                .Text("Wykres: podział według kategorii")
                                .Bold()
                                .FontSize(13);

                            col.Item()
                                .Border(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .CornerRadius(6)
                                .Padding(10)
                                .Row(row =>
                                {
                                    // LEWA: kontrolowany rozmiar wykresu (żeby nie robił "baneru")
                                    row.ConstantItem(260)
                                       .AlignMiddle()
                                       .AlignCenter()
                                       .Element(e => e
                                           .Width(240)
                                           .Height(200)
                                           .Image(donutChartPng)
                                           .FitArea()
                                       );

                                    row.Spacing(12);

                                    // PRAWA: legenda jako tabela
                                    row.RelativeItem().Element(c => LegendSection(c, vm));
                                });
                        }

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

        private static void LegendSection(IContainer container, ReportsViewModel vm)
        {
            var details = vm.Details ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.CategoryAmount>();

            var items = details
                .OrderByDescending(x => x.Amount)
                .Take(10) // legenda max 10 pozycji
                .ToList();

            container.Column(col =>
            {
                col.Spacing(4);

                col.Item().Text("Legenda").SemiBold();

                if (items.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(12);  // "kropka"
                        cols.RelativeColumn(4);   // nazwa
                        cols.RelativeColumn(2);   // kwota
                        cols.RelativeColumn(1);   // %
                    });

                    t.Header(h =>
                    {
                        h.Cell().Text("");
                        h.Cell().Text("Kategoria").SemiBold();
                        h.Cell().AlignRight().Text("Kwota").SemiBold();
                        h.Cell().AlignRight().Text("%").SemiBold();
                    });

                    foreach (var it in items)
                    {
                        // ZAMIENNIK DrawEllipse: mały zaokrąglony kwadrat jako "kropka"
                        t.Cell().AlignMiddle().Element(e => e
                            .Width(10)
                            .Height(10)
                            .Background(Colors.Grey.Darken2)
                            .CornerRadius(5)
                        );

                        t.Cell().Text(it.Name).FontSize(10);
                        t.Cell().AlignRight().Text(it.Amount.ToString("N2") + " zł").FontSize(10);
                        t.Cell().AlignRight().Text(it.SharePercent.ToString("N1")).FontSize(10);
                    }
                });

                if (details.Count > items.Count)
                {
                    col.Item()
                        .PaddingTop(4)
                        .Text($"Pozostałe kategorie: {details.Count - items.Count}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                }
            });
        }

        // ===== Sekcje dokumentu =====

        private static void KpiSection(IContainer container, ReportsViewModel vm)
        {
            container.Column(col =>
            {
                col.Item().Text("Podsumowanie KPI")
                    .Bold()
                    .FontSize(13);

                var kpis = vm.KPIList ?? new System.Collections.ObjectModel.ObservableCollection<KeyValuePair<string, string>>();
                if (kpis.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

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

                    foreach (var kv in kpis)
                    {
                        table.Cell().Element(c => c.Padding(4)).Text(kv.Key);
                        table.Cell().Element(c => c.Padding(4)).Text(kv.Value);
                    }
                });
            });
        }

        private static void CategoriesSection(IContainer container, ReportsViewModel vm)
        {
            var details = vm.Details ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.CategoryAmount>();

            container.Column(col =>
            {
                col.Item().Text("Kategorie wydatków (ten okres)")
                    .Bold()
                    .FontSize(13);

                if (details.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

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

                    foreach (var row in details)
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
            var transactions = (vm.FilteredTransactions ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.TransactionDto>())
                .OrderByDescending(t => t.Date)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Transakcje (ten okres)")
                    .Bold()
                    .FontSize(13);

                if (transactions.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

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
            var insights = vm.Insights ?? new System.Collections.ObjectModel.ObservableCollection<string>();
            if (insights.Count == 0)
                return;

            container.Column(col =>
            {
                col.Item().Text("Wnioski finansowe")
                    .Bold()
                    .FontSize(13);

                foreach (var insight in insights)
                    col.Item().Text("• " + insight);
            });
        }

        // ===== Lekkie modele danych na potrzeby prostego eksportu (zostawiam bez zmian) =====

        public static string ExportPeriodReport(
            DateTime start,
            DateTime end,
            PeriodSummary curr,
            PeriodSummary prev,
            IEnumerable<CategoryDetail> categories,
            Dictionary<string, decimal> chartTotals)
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"Finly_Raport_{start:yyyyMMdd}-{end:yyyyMMdd}.pdf");

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Text($"Raport finansowy – {start:dd.MM.yyyy} – {end:dd.MM.yyyy}")
                        .FontSize(20).Bold().AlignLeft();

                    page.Content().Column(col =>
                    {
                        col.Item().Text("Podsumowanie bieżącego okresu")
                            .FontSize(16).Bold();

                        col.Item().Text($@"
Suma wydatków: {curr.Expenses:N2} zł
Suma przychodów: {curr.Incomes:N2} zł
Saldo: {curr.Saldo:N2} zł
").FontSize(12);

                        col.Item().PaddingTop(10).Text("Podsumowanie poprzedniego okresu")
                            .FontSize(16).Bold();

                        col.Item().Text($@"
Suma wydatków: {prev.Expenses:N2} zł
Suma przychodów: {prev.Incomes:N2} zł
Saldo: {prev.Saldo:N2} zł
").FontSize(12);

                        col.Item().PaddingTop(10).Text("Porównanie okresów")
                            .FontSize(16).Bold();

                        col.Item().Text($@"
Zmiana wydatków: {curr.ExpensesChangeText}
Zmiana przychodów: {curr.IncomesChangeText}
Zmiana salda: {curr.SaldoChangeText}
").FontSize(12);

                        col.Item().PaddingTop(10).Text("Kategorie").FontSize(16).Bold();

                        if (categories != null)
                        {
                            foreach (var c in categories)
                                col.Item().Text($"{c.Name} – {c.Amount:N2} zł ({c.Percent:N2}%)");
                        }
                    });
                });
            });

            doc.GeneratePdf(path);
            return path;
        }

        public class PeriodSummary
        {
            public decimal Expenses { get; set; }
            public decimal Incomes { get; set; }
            public decimal Saldo { get; set; }
            public string ExpensesChangeText { get; set; } = string.Empty;
            public string IncomesChangeText { get; set; } = string.Empty;
            public string SaldoChangeText { get; set; } = string.Empty;
        }

        public class CategoryDetail
        {
            public string Name { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public decimal Percent { get; set; }
        }
    }
}
