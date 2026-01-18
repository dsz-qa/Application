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
    /// Eksport raportu finansowego do PDF (QuestPDF) – wersja dopasowana do aktualnego ReportsViewModel.
    /// </summary>
    public static class PdfExportService
    {
        public static string ExportReportsPdf(ReportsViewModel vm)
            => ExportReportsPdf(vm, donutChartPng: null);

        /// <summary>
        /// donutChartPng – opcjonalny PNG wykresu (np. donut) wygenerowany w UI.
        /// Jeśli null – PDF nadal generuje tabele i podsumowania.
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

                    page.Header().Column(h =>
                    {
                        h.Item().Text("Finly – raport finansowy").SemiBold().FontSize(18);
                        h.Item().Text($"Okres: {vm.FromDate:dd.MM.yyyy} – {vm.ToDate:dd.MM.yyyy}")
                              .FontColor(Colors.Grey.Darken2);
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().Element(c => KpiSection(c, vm));

                        // Opcjonalny obraz (np. donut z UI)
                        if (donutChartPng != null && donutChartPng.Length > 0)
                        {
                            col.Item().PaddingTop(10).Text("Wykres (opcjonalnie)").Bold().FontSize(13);

                            col.Item()
                               .Border(1)
                               .BorderColor(Colors.Grey.Lighten2)
                               .CornerRadius(6)
                               .Padding(10)
                               .Height(260)
                               .Image(donutChartPng)
                               .FitArea();
                        }

                        col.Item().PaddingTop(10).Element(c => CategoriesSection(c, vm));
                        col.Item().PaddingTop(10).Element(c => TransactionsSection(c, vm));
                        col.Item().PaddingTop(10).Element(c => BudgetsSection(c, vm));
                        col.Item().PaddingTop(10).Element(c => LoansSection(c, vm));
                        col.Item().PaddingTop(10).Element(c => GoalsSection(c, vm));
                        col.Item().PaddingTop(10).Element(c => PlannedSection(c, vm));
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

        private static string BuildDefaultReportFileName(DateTime from, DateTime to)
            => $"finly-raport-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf";

        // =========================
        // KPI
        // =========================

        private static void KpiSection(IContainer container, ReportsViewModel vm)
        {
            // Budżety OK / over liczymy z vm.Budgets
            var budgetsOk = (vm.Budgets ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.BudgetRow>())
                .Count(b => !b.IsOver);
            var budgetsOver = (vm.Budgets ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.BudgetRow>())
                .Count(b => b.IsOver);

            container.Column(col =>
            {
                col.Item().Text("Podsumowanie KPI").Bold().FontSize(13);

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(3);
                    });

                    Row(t, "Obecny okres – wydatki", vm.TotalExpensesStr);
                    Row(t, "Obecny okres – przychody", vm.TotalIncomesStr);
                    Row(t, "Obecny okres – bilans", vm.BalanceStr);

                    Row(t, "Poprzedni okres – wydatki", vm.PreviousTotalExpensesStr);
                    Row(t, "Poprzedni okres – przychody", vm.PreviousTotalIncomesStr);
                    Row(t, "Poprzedni okres – bilans", vm.PreviousBalanceStr);

                    Row(t, "Zmiana wydatków (delta)", vm.DeltaExpensesStr);
                    Row(t, "Zmiana przychodów (delta)", vm.DeltaIncomesStr);
                    Row(t, "Zmiana bilansu (delta)", vm.DeltaBalanceStr);

                    Row(t, "Budżety OK", budgetsOk.ToString());
                    Row(t, "Budżety przekroczone", budgetsOver.ToString());

                    Row(t, "Delta salda (symulacja planowanych)", vm.SimBalanceDeltaStr);
                });
            });

            static void Row(TableDescriptor t, string k, string v)
            {
                t.Cell().Element(c => c.Padding(4)).Text(k).SemiBold();
                t.Cell().Element(c => c.Padding(4)).Text(v);
            }
        }

        // =========================
        // Kategorie – wyliczane z Rows (bez CategoryBreakdown w VM)
        // =========================

        private sealed class CatRow
        {
            public string Name { get; set; } = "";
            public decimal Amount { get; set; }
            public decimal SharePercent { get; set; }
        }

        private static void CategoriesSection(IContainer container, ReportsViewModel vm)
        {
            var rows = vm.Rows?.ToList() ?? new List<Finly.Services.SpecificPages.ReportsService.ReportItem>();

            // Jedna, prosta tabela: TOP 12 kategorii z sumy absolutów (wszystkie typy)
            var total = rows.Sum(r => Math.Abs(r.Amount));
            var cat = rows
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "(brak kategorii)" : r.Category)
                .Select(g => new CatRow
                {
                    Name = g.Key,
                    Amount = g.Sum(x => Math.Abs(x.Amount)),
                    SharePercent = total <= 0 ? 0 : (g.Sum(x => Math.Abs(x.Amount)) / total * 100m)
                })
                .OrderByDescending(x => x.Amount)
                .Take(12)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Kategorie – TOP 12 (suma transakcji w okresie)").Bold().FontSize(13);

                if (cat.Count == 0)
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

                    foreach (var r in cat)
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
                    h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold())
                                          .Padding(4).Background(Colors.Grey.Lighten3)).Text(c1);
                    h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold())
                                          .Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text(c2);
                    h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold())
                                          .Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text(c3);
                });
            }
        }

        // =========================
        // Transakcje
        // =========================

        private static void TransactionsSection(IContainer container, ReportsViewModel vm)
        {
            var tx = (vm.Rows ?? new System.Collections.ObjectModel.ObservableCollection<Finly.Services.SpecificPages.ReportsService.ReportItem>())
                .OrderByDescending(x => x.Date)
                .Take(40)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Transakcje – ostatnie 40 w okresie").Bold().FontSize(13);

                if (tx.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(75);  // data
                        cols.ConstantColumn(70);  // typ
                        cols.RelativeColumn(3);   // kategoria
                        cols.RelativeColumn(2);   // kwota
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .Text("Data");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .Text("Typ");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .Text("Kategoria");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .AlignRight()
                            .Text("Kwota");
                    });


                    foreach (var r in tx)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(r.Date.ToString("dd.MM.yyyy"));
                        t.Cell().Element(c => c.Padding(4)).Text(r.Type);
                        t.Cell().Element(c => c.Padding(4)).Text(r.Category);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text($"{r.Amount:N2} zł");
                    }
                });
            });

        }

        // =========================
        // Budżety (vm.Budgets)
        // =========================

        private static void BudgetsSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.Budgets ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.BudgetRow>())
                .OrderByDescending(x => x.Planned)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Budżety").Bold().FontSize(13);

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
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .Text("Budżet");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .AlignRight()
                            .Text("Plan");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .AlignRight()
                            .Text("Wydane");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3))
                            .AlignRight()
                            .Text("Pozostało");
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

        // =========================
        // Kredyty (vm.Loans) – dopasowane do LoanRow, który masz teraz
        // =========================

        private static void LoansSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.Loans ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.LoanRow>())
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Kredyty").Bold().FontSize(13);

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
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Nazwa");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Pozostało");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Spłacono");
                        h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Nadpłaty");
                    });

                    foreach (var l in rows)
                    {
                        t.Cell().Element(c => c.Padding(4)).Text(l.Name);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(l.RemainingToPayStr);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(l.PaidInPeriodStr);
                        t.Cell().Element(c => c.Padding(4)).AlignRight().Text(l.OverpaidInPeriodStr);
                    }
                });
            });
        }

        // =========================
        // Cele (vm.Goals)
        // =========================

        private static void GoalsSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.Goals ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.GoalRow>())
                .OrderByDescending(x => x.Target)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Cele").Bold().FontSize(13);

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

                // Rekomendacje next period (opcjonalnie – masz to w VM)
                var rec = rows.Where(x => x.NeededNextPeriod > 0).OrderByDescending(x => x.NeededNextPeriod).Take(8).ToList();
                if (rec.Count > 0)
                {
                    col.Item().PaddingTop(6).Text("Rekomendacja na kolejny okres (aby domknąć cele)").SemiBold();

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(4);
                            cols.RelativeColumn(2);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).Text("Cel");
                            h.Cell().Element(c => c.DefaultTextStyle(x => x.SemiBold()).Padding(4).Background(Colors.Grey.Lighten3)).AlignRight().Text("Kwota");
                        });

                        foreach (var r in rec)
                        {
                            t.Cell().Element(c => c.Padding(4)).Text(r.Name);
                            t.Cell().Element(c => c.Padding(4)).AlignRight().Text(r.NeededNextPeriodStr);
                        }
                    });
                }
            });
        }

        // =========================
        // Symulacja (vm.PlannedSim)
        // =========================

        private static void PlannedSection(IContainer container, ReportsViewModel vm)
        {
            var rows = (vm.PlannedSim ?? new System.Collections.ObjectModel.ObservableCollection<ReportsViewModel.PlannedRow>())
                .OrderBy(x => x.Date)
                .Take(40)
                .ToList();

            container.Column(col =>
            {
                col.Item().Text("Symulacja – planowane transakcje (do 40)").Bold().FontSize(13);
                col.Item().Text($"Delta salda (symulacja): {vm.SimBalanceDeltaStr}")
                          .FontColor(Colors.Grey.Darken2);

                if (rows.Count == 0)
                {
                    col.Item().Text("(brak danych)").FontColor(Colors.Grey.Darken1);
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(75);
                        cols.ConstantColumn(90);
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
