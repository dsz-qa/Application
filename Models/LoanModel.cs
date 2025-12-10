using System;

namespace Finly.Models
{
    public class LoanModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public string Name { get; set; } = "";

        /// <summary>
        /// Aktualny kapita³ do sp³aty (saldo kredytu na dziœ).
        /// </summary>
        public decimal Principal { get; set; }

        /// <summary>
        /// Oprocentowanie nominalne roczne, np. 6.28.
        /// </summary>
        public decimal InterestRate { get; set; } = 0m;

        /// <summary>
        /// Data startu kredytu (lub stan pocz¹tkowy, od którego Finly go œledzi).
        /// </summary>
        public DateTime StartDate { get; set; } = DateTime.Today;

        /// <summary>
        /// Liczba miesiêcy (rat) kredytu – na tym etapie traktujemy to jako liczba rat pozosta³ych.
        /// </summary>
        public int TermMonths { get; set; } = 0;

        /// <summary>
        /// Dzieñ miesi¹ca, w którym przypada rata (1–31, 0 = nieustawiony).
        /// </summary>
        public int PaymentDay { get; set; } = 0;

        public string? Note { get; set; }

        // ================= NOWE POLA =================

        /// <summary>
        /// Data ostatniego rozliczenia odsetek (ostatnia rata / nadp³ata).
        /// Od tej daty liczymy kolejne odsetki dzienne.
        /// </summary>
        public DateTime LastSettlementDate { get; set; } = DateTime.Today;

        /// <summary>
        /// (Opcjonalne) liczba rat pozosta³ych do koñca – jeœli chcesz mieæ to
        /// osobno od TermMonths. Na pocz¹tek mo¿esz ustawiaæ to samo co TermMonths.
        /// </summary>
        public int RemainingInstallments { get; set; } = 0;
    }

    public enum LoanOperationType
    {
        Installment,   // normalna rata
        Overpayment,   // nadp³ata
        Fee,           // op³ata
        RateChange     // zmiana oprocentowania
    }

    public class LoanOperationModel
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public DateTime Date { get; set; }
        public LoanOperationType Type { get; set; }

        public decimal TotalAmount { get; set; }      // ca³a kwota z konta (-2615,69)
        public decimal CapitalPart { get; set; }      // czêœæ kapita³owa
        public decimal InterestPart { get; set; }     // odsetki
        public decimal RemainingPrincipal { get; set; } // saldo po tej operacji
    }

}
