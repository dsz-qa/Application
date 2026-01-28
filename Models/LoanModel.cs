using System;

namespace Finly.Models
{
    /// <summary>
    /// Encja kredytu zgodna z DB (kontrakt).
    /// Snapshoty (saldo, % sp³aty, pozosta³e raty itd.) liczysz z harmonogramu i trzymasz w VM.
    /// </summary>
    public class LoanModel
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Kwota pocz¹tkowa / kapita³ kredytu.
        /// </summary>
        public decimal Principal { get; set; }

        /// <summary>
        /// Oprocentowanie roczne (np. 7.25 = 7.25%).
        /// </summary>
        public decimal InterestRate { get; set; } = 0m;

        public DateTime StartDate { get; set; } = DateTime.Today;

        /// <summary>
        /// Liczba miesiêcy umowy (np. 360).
        /// </summary>
        public int TermMonths { get; set; } = 0;

        /// <summary>
        /// Dzieñ miesi¹ca p³atnoœci raty (1-31). 0 jeœli nieznany.
        /// </summary>
        public int PaymentDay { get; set; } = 0;

        public string? Note { get; set; }

        /// <summary>
        /// Œcie¿ka do CSV harmonogramu w systemie plików (persist w DB).
        /// </summary>
        public string? SchedulePath { get; set; }

        /// <summary>
        /// Sk¹d ksiêgujemy raty (portfel/konto/koperta).
        /// </summary>
        public PaymentKind PaymentKind { get; set; } = PaymentKind.FreeCash;

        /// <summary>
        /// Id referencji zale¿nie od PaymentKind:
        /// - BankAccount => BankAccountId
        /// - Envelope    => EnvelopeId
        /// - FreeCash/SavedCash => null
        /// </summary>
        public int? PaymentRefId { get; set; }
    }

    // Zostawiamy, jeœli u¿ywasz operacji w analizach/historii.
    // One nie musz¹ byæ 1:1 DB, ale jeœli zapisujesz je w DB,
    // wtedy dopasuj typy/kolumny w SchemaService.
    public enum LoanOperationType
    {
        Installment,
        Overpayment,
        Fee,
        RateChange
    }

    public class LoanOperationModel
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public DateTime Date { get; set; }
        public LoanOperationType Type { get; set; }

        public decimal TotalAmount { get; set; }
        public decimal CapitalPart { get; set; }
        public decimal InterestPart { get; set; }

        /// <summary>
        /// Pozosta³y kapita³ po operacji (jeœli wyliczasz z harmonogramu, to jest snapshot).
        /// </summary>
        public decimal RemainingPrincipal { get; set; }
    }
}
