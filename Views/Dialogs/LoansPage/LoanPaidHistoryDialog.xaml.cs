using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Finly.Models;
using Finly.Services.Features;

namespace Finly.Views.Dialogs
{
    public partial class LoanPaidHistoryDialog : Window
    {
        private static readonly CultureInfo Pl = new("pl-PL");

        private sealed class RowVm
        {
            public string Type { get; init; } = "—";
            public string No { get; init; } = "—";
            public string Date { get; init; } = "—";
            public string Principal { get; init; } = "—";
            public string Interest { get; init; } = "—";
            public string Total { get; init; } = "—";
            public string Remaining { get; init; } = "—";
        }

        public LoanPaidHistoryDialog(
            string loanName,
            IReadOnlyList<LoanInstallmentRow> paidScheduleRows,
            int userId,
            int loanId)
        {
            InitializeComponent();

            TitleText.Text = "Historia spłaconych rat i nadpłat";
            HeaderText.Text = $"Historia – {loanName}";
            TopLineText.Text = "spłacone raty + nadpłaty";

            paidScheduleRows ??= Array.Empty<LoanInstallmentRow>();

            // 1) Raty zapłacone: preferuj DB (Status=1). Jeśli pusto -> fallback do listy z harmonogramu.
            var paidDb = DatabaseService.GetPaidInstallmentsByLoan(userId, loanId);

            // map: nr raty -> harmonogram (żeby uzupełniać kapitał/odsetki/saldo jeśli DB ma null)
            var schedMap = paidScheduleRows
                .Where(x => x.InstallmentNo.HasValue && x.InstallmentNo.Value > 0)
                .GroupBy(x => x.InstallmentNo!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Date).First());

            var rows = new List<(DateTime date, RowVm vm)>();

            if (paidDb.Count > 0)
            {
                foreach (var inst in paidDb.OrderBy(x => x.DueDate).ThenBy(x => x.InstallmentNo))
                {
                    schedMap.TryGetValue(inst.InstallmentNo, out var srow);

                    decimal? cap = inst.PrincipalAmount ?? srow?.Principal;
                    decimal? intr = inst.InterestAmount ?? srow?.Interest;
                    decimal? rem = inst.RemainingBalance ?? srow?.Remaining;

                    rows.Add((inst.DueDate.Date, new RowVm
                    {
                        Type = "Rata",
                        No = inst.InstallmentNo > 0 ? inst.InstallmentNo.ToString() : "—",
                        Date = inst.DueDate.ToString("dd.MM.yyyy"),
                        Principal = cap.HasValue ? cap.Value.ToString("N2", Pl) : "—",
                        Interest = intr.HasValue ? intr.Value.ToString("N2", Pl) : "—",
                        Total = inst.TotalAmount.ToString("N2", Pl),
                        Remaining = rem.HasValue ? rem.Value.ToString("N2", Pl) : "—"
                    }));
                }
            }
            else
            {
                // fallback: pokazujemy “zapłacone” z harmonogramu (Twoja logika: 2,3 bo kolejna 4)
                foreach (var r in paidScheduleRows.OrderBy(x => x.Date))
                {
                    rows.Add((r.Date.Date, new RowVm
                    {
                        Type = "Rata",
                        No = r.InstallmentNo?.ToString() ?? "—",
                        Date = r.Date.ToString("dd.MM.yyyy"),
                        Principal = r.Principal.HasValue ? r.Principal.Value.ToString("N2", Pl) : "—",
                        Interest = r.Interest.HasValue ? r.Interest.Value.ToString("N2", Pl) : "—",
                        Total = r.Total.ToString("N2", Pl),
                        Remaining = r.Remaining.HasValue ? r.Remaining.Value.ToString("N2", Pl) : "—"
                    }));
                }
            }

            // 2) Nadpłaty: z tabeli LoanOperations (Type=Overpayment)
            var ops = DatabaseService.GetLoanOperations(userId, loanId)
                .Where(o => o.Type == LoanOperationType.Overpayment)
                .OrderBy(o => o.Date)
                .ToList();

            foreach (var op in ops)
            {
                rows.Add((op.Date.Date, new RowVm
                {
                    Type = "Nadpłata",
                    No = "—",
                    Date = op.Date.ToString("dd.MM.yyyy"),
                    Principal = op.CapitalPart.ToString("N2", Pl),
                    Interest = op.InterestPart.ToString("N2", Pl),
                    Total = op.TotalAmount.ToString("N2", Pl),
                    Remaining = op.RemainingPrincipal.ToString("N2", Pl)
                }));
            }

            var ordered = rows.OrderBy(x => x.date).Select(x => x.vm).ToList();
            RowsMiniTable.ItemsSource = ordered;

            // 3) Podsumowania
            int paidCount = ordered.Count(x => x.Type == "Rata");
            int overpayCount = ordered.Count(x => x.Type == "Nadpłata");

            decimal sumPaid = ordered
                .Where(x => x.Type == "Rata")
                .Select(x => TryParseMoney(x.Total))
                .Sum();

            decimal sumOver = ordered
                .Where(x => x.Type == "Nadpłata")
                .Select(x => TryParseMoney(x.Total))
                .Sum();

            SubText.Text = $"Pozycje: {ordered.Count} (Raty: {paidCount}, Nadpłaty: {overpayCount}).";
            SummaryText.Text = $"Suma rat: {sumPaid.ToString("N2", Pl)} zł  •  Suma nadpłat: {sumOver.ToString("N2", Pl)} zł";
        }

        private static decimal TryParseMoney(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s.Replace(" ", ""), NumberStyles.Number, Pl, out var v)) return v;
            if (decimal.TryParse(s.Replace(" ", "").Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out v)) return v;
            return 0m;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
