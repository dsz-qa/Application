using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Finly.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Finly.Services
{
    /// <summary>
    /// Serwis odpowiedzialny za eksport pełnego raportu finansowego do PDF (QuestPDF).
    /// WERSJA zgodna z aktualnym ReportsViewModel (bez: KPIList/Details/FilteredTransactions/Insights/ComparisonPeriodLabel).
    /// </summary>
    public static class PdfExportService
    {
        public static string ExportReportsPdf(ReportsViewModel vm)
            => ExportReportsPdf(vm, null);

        /// <summary>
        /// donutChartPng – PNG wykresu donut (opcjonalnie).
        /// </summary>
        public static string ExportReportsPdf(ReportsViewModel vm, byte[]? donutChartPng)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var fileName = BuildDefaultReportFileName(vm.FromDate, vm.ToDate);
            var path = Path.Combine(desktop, fileName);

            QuestPDF.Settings.License = LicenseType.Community;

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

                        col.Item().Element(c => KpiSection(c, vm));

                        // ===== WYKRES DONUT (opcjonalnie) =====
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
                               .Height(270)
                               .Image(donutChartPng)
                               .FitArea();
                        }

                        col.Item().Element(c => CategoriesSection(c, vm));
                        col.Item().Element(c => TransactionsSection(c, vm));

                        // Opcjonalne zakładki (jeśli chcesz mieć w PDF):
                        col.Item().Element(c => BudgetsSection(c, vm));
                        col.Item().Element(c => LoansSection(c, vm));
                        col.Item().Element(c => GoalsSection(c, vm));
                        col.Item().Element(c => PlannedSection(c, vm));
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

        // =========================
        // Helpers
        // =========================

        private static string BuildDefaultReportFileName(DateTime from, DateTime to)
            => $"finly-raport-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf";

        // =========================
        // Sekcje PDF
        // =========================

        private static void KpiSection(IContainer container, ReportsViewModel vm)
        {
            container.Column(col =>
            {
                col.Item().Text("Podsumowanie KPI")
                    .Bold()
                    .FontSize(13);

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(3);
                    });

                    Row(t, "Wydatki", vm.TotalExpensesStr);
                    Row(t, "Przychody", vm.TotalIncomesStr);
                    Row(t, "Saldo", vm.BalanceStr);
                    Row(t, "Budżety OK", vm.BudgetsOkCount.ToString());
                    Row(t, "Budżety przekroczone", vm.BudgetsOverCount.ToString());
                    Row(t, "Delta salda (planowane)", vm.SimBalanceDeltaStr);
                });
            });

            static void Row(TableDescriptor t, string k, string v)
            {
                t.Cell().Element(c => c.Padding(4)).Text(k).SemiBold();
                t.Cell().Element(c => c.Padding(4)).Text(v);
            }
        }

        private static void CategoriesSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.CategoryBreakdown ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.CategoryAmount>())
                .OrderByDescending(x => x.Amount)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Kategorie (wg filtra typu transakcji)")
                    .Bold()
                    .FontSize(13);

                if (rows.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(4);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });

                    Header(t, "Kategoria", "Kwota", "Udział %");

                    foreach (var r in rows)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(r.Name);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text($"{r.Amount:N2} zł");
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text($"{r.SharePercent:N1}");
                    }
                });
            });

            static void Header(TableDescriptor t, string c1, string c2, string c3)
            {
                t.Header(h =>
                {
                    h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text(c1);
                    h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text(c2);
                    h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text(c3);
                });
            }
        }

        private static void TransactionsSection(IContainer container, ReportsViewModel vm)
        {
            var tx = (vm.Rows ?? new System.Collections.ObjectModel.ObservableCollection<Finly.Services.SpecificPages.ReportsService.ReportItem>())
                .OrderByDescending(x => x.Date)
                .Take(40) // ograniczamy PDF, żeby nie był gigantyczny
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Transakcje (po filtrach) – ostatnie 40")
                    .Bold()
                    .FontSize(13);

                if (tx.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(70);  // data
                        cols.ConstantColumn(70);  // typ
                        cols.RelativeColumn(3);   // kategoria
                        cols.RelativeColumn(2);   // kwota
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Data");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Typ");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Kategoria");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Kwota");
                    });

                    foreach (var r in tx)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(r.Date.ToString("dd.MM.yyyy"));
                        t.Cell().Element(c => c.Padding(4)).Text(r.Type);
                        t.Cell().Element(c => c.Padding(4)).Text(r.Category);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text($"{r.Amount:N2}");
                    }
                });
            });
        }

        private static void BudgetsSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.Budgets ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.BudgetRow>()).ToList();

            container.Column(col =>
            {
                col.Item().Text("Budżety")
                    .Bold()
                    .FontSize(13);

                if (rows.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(4);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Budżet");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Plan");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Wydane");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Pozostało");
                    });

                    foreach (var b in rows)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(b.Name);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(b.PlannedStr);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(b.SpentStr);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(b.RemainingStr);
                    }
                });
            });
        }

        private static void LoansSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.Loans ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.LoanRow>()).ToList();

            container.Column(col =>
            {
                col.Item().Text("Kredyty")
                    .Bold()
                    .FontSize(13);

                if (rows.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(4);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Nazwa");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Rata (est.)");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Spłacono");
                    });

                    foreach (var l in rows)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(l.Name);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(l.EstimatedMonthlyPaymentStr);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(l.PaidInPeriodStr);
                    }
                });
            });
        }

        private static void GoalsSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.Goals ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.GoalRow>()).ToList();

            container.Column(col =>
            {
                col.Item().Text("Cele")
                    .Bold()
                    .FontSize(13);

                if (rows.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(4);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Cel");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Docelowo");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Aktualnie");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Brakuje");
                    });

                    foreach (var g in rows)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(g.Name);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(g.TargetStr);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(g.CurrentStr);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(g.MissingStr);
                    }
                });
            });
        }

        private static void PlannedSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.PlannedSim ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.PlannedRow>())
                .OrderBy(x => x.Date)
                .Take(40)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Symulacja – planowane transakcje (do 40)")
                    .Bold()
                    .FontSize(13);

                if (rows.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(70);
                        cols.ConstantColumn(70);
                        cols.RelativeColumn(4);
                        cols.RelativeColumn(2);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Data");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Typ");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Opis");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Kwota");
                    });

                    foreach (var r in rows)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(r.Date.ToString("dd.MM.yyyy"));
                        t.Cell().Element(c => c.Padding(4)).Text(r.Type);
                        t.Cell().Element(c => c.Padding(4)).Text(r.Description);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(r.AmountStr);
                    }
                });
            });
        }
    }
}
