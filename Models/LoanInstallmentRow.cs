using System;

namespace Finly.Models
{
    public sealed class LoanInstallmentRow
    {
        public DateTime Date { get; init; }
        public decimal Total { get; init; }
        public decimal? Principal { get; init; }
        public decimal? Interest { get; init; }
        public decimal? Remaining { get; init; }

        public override string ToString()
            => $"{Date:dd.MM.yyyy} | rata: {Total:N2} z³"
               + (Principal is null ? "" : $" | kapita³: {Principal:N2} z³")
               + (Interest is null ? "" : $" | odsetki: {Interest:N2} z³")
               + (Remaining is null ? "" : $" | saldo: {Remaining:N2} z³");
    }
}

