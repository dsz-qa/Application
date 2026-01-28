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
    public partial class LoanScheduleDialog : Window
    {
        private static readonly CultureInfo Pl = new("pl-PL");

        private sealed class RowVm
        {
            public string No { get; init; } = "—";
            public string Date { get; init; } = "—";
            public string Principal { get; init; } = "—";
            public string Interest { get; init; } = "—";
            public string Total { get; init; } = "—";
            public string Remaining { get; init; } = "—";
        }

        // ✅ kompatybilność: stary konstruktor działa jak wcześniej
        public LoanScheduleDialog(string loanName, IReadOnlyList<LoanInstallmentRow> rows)
            : this(loanName, rows, userId: null, loanId: null)
        {
        }

        // ✅ nowy konstruktor: opcjonalnie pokazuje validator z DB (planned/paid)
        public LoanScheduleDialog(string loanName, IReadOnlyList<LoanInstallmentRow> rows, int? userId, int? loanId)
        {
            InitializeComponent();

            TitleText.Text = "Harmonogram spłat";
            HeaderText.Text = $"Harmonogram – {loanName}";

            rows ??= Array.Empty<LoanInstallmentRow>();
            var list = rows.OrderBy(r => r.Date).ToList();

            // ===== Next payment =====
            var today = DateTime.Today;
            var next = list.Where(r => r.Date >= today).OrderBy(r => r.Date).FirstOrDefault();

            NextPaymentText.Text = next != null
                ? $"{next.Total.ToString("N2", Pl)} zł · {next.Date:dd.MM.yyyy}"
                : "Brak przyszłych rat w pliku.";

            // ===== Podsumowania =====
            SubText.Text = $"Liczba rat w harmonogramie: {list.Count}.";

            var remainingRows = list.Where(r => r.Date >= today).ToList();
            int remainingCount = remainingRows.Count;

            DateTime? payoffDate = list.Count > 0 ? list.Max(r => r.Date) : (DateTime?)null;

            decimal paidSoFar = list.Where(r => r.Date < today).Sum(r => r.Total);
            decimal remainingSum = remainingRows.Sum(r => r.Total);

            // ✅ VALIDATOR: planned/paid z tabeli LoanInstallments (DEV helper)
            string validator = "";
            if (userId.HasValue && loanId.HasValue && userId.Value > 0 && loanId.Value > 0)
            {
                try
                {
                    var (planned, paid) = DatabaseService.GetLoanInstallmentsStatusCounts(userId.Value, loanId.Value);
                    validator = $"  •  [DB] Planned: {planned}, Paid: {paid}";
                    System.Diagnostics.Debug.WriteLine($"Installments (LoanId={loanId}): planned={planned}, paid={paid}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Validator read failed: " + ex);
                    // nie psujemy UI – validator jest tylko pomocniczy
                }
            }

            SummaryText.Text =
                $"Liczba rat pozostałych: {remainingCount}  •  " +
                $"Termin spłacenia kredytu: {(payoffDate is null ? "—" : payoffDate.Value.ToString("dd.MM.yyyy"))}  •  " +
                $"Spłacono do dziś (kapitał+odsetki): {paidSoFar.ToString("N2", Pl)} zł  •  " +
                $"Pozostało do spłaty: {remainingSum.ToString("N2", Pl)} zł" +
                validator;

            // ===== MiniTable =====
            RowsMiniTable.ItemsSource = list.Select(r => new RowVm
            {
                No = r.InstallmentNo?.ToString() ?? "—",
                Date = r.Date.ToString("dd.MM.yyyy"),
                Principal = r.Principal.HasValue ? r.Principal.Value.ToString("N2", Pl) : "—",
                Interest = r.Interest.HasValue ? r.Interest.Value.ToString("N2", Pl) : "—",
                Total = r.Total.ToString("N2", Pl),
                Remaining = r.Remaining.HasValue ? r.Remaining.Value.ToString("N2", Pl) : "—"
            }).ToList();
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
