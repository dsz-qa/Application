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

            _ = paidScheduleRows;

            TitleText.Text = "Historia spłaconych rat i nadpłat";
            HeaderText.Text = $"Historia – {loanName}";
            TopLineText.Text = "spłacone raty + nadpłaty";

            var rows = new List<(DateTime date, RowVm vm)>();

            var paidDb = DatabaseService.GetLoanInstallments(userId, loanId)
                .Where(x => x.Status == 1)
                .OrderBy(x => x.PaidAt ?? x.DueDate)
                .ThenBy(x => x.InstallmentNo)
                .ToList();

            foreach (var inst in paidDb)
            {
                var sortDate = (inst.PaidAt ?? inst.DueDate).Date;

                rows.Add((sortDate, new RowVm
                {
                    Type = "Rata",
                    No = inst.InstallmentNo > 0 ? inst.InstallmentNo.ToString() : "—",
                    Date = sortDate.ToString("dd.MM.yyyy"),
                    Principal = inst.PrincipalAmount.HasValue ? inst.PrincipalAmount.Value.ToString("N2", Pl) : "—",
                    Interest = inst.InterestAmount.HasValue ? inst.InterestAmount.Value.ToString("N2", Pl) : "—",
                    Total = inst.TotalAmount.ToString("N2", Pl),
                    Remaining = inst.RemainingBalance.HasValue ? inst.RemainingBalance.Value.ToString("N2", Pl) : "—"
                }));
            }

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

            var ordered = rows
                .OrderBy(x => x.date)
                .ThenBy(x => x.vm.Type == "Rata" ? 0 : 1)
                .Select(x => x.vm)
                .ToList();

            RowsMiniTable.ItemsSource = ordered;

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

            if (ordered.Count == 0)
                TopLineText.Text = "brak zapisanych spłat i nadpłat";
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
