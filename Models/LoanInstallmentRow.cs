using System;

namespace Finly.Models
{
    public sealed class LoanInstallmentRow
    {
        public int? InstallmentNo { get; set; }   // <= wa¿ne: set, nie init

        public DateTime Date { get; set; }
        public decimal Total { get; set; }
        public decimal? Principal { get; set; }
        public decimal? Interest { get; set; }
        public decimal? Remaining { get; set; }

        public override string ToString()
            => $"{(InstallmentNo is null ? "—" : InstallmentNo.ToString())} | {Date:dd.MM.yyyy} | rata: {Total:N2} z³"
               + (Principal is null ? "" : $" | kapita³: {Principal:N2} z³")
               + (Interest is null ? "" : $" | odsetki: {Interest:N2} z³")
               + (Remaining is null ? "" : $" | saldo: {Remaining:N2} z³");
    }
}

