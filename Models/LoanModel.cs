using System;

namespace Finly.Models
{
    public class LoanModel
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; } = "";

        public decimal Principal { get; set; }

        public decimal InterestRate { get; set; } = 0m;

        public DateTime StartDate { get; set; } = DateTime.Today;

        public int TermMonths { get; set; } = 0;

        public int PaymentDay { get; set; } = 0;

        public string? Note { get; set; }

        public DateTime LastSettlementDate { get; set; } = DateTime.Today;

        public int RemainingInstallments { get; set; } = 0;
    }

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
        public decimal RemainingPrincipal { get; set; }
    }

}
