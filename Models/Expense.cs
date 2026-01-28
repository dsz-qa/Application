using System;

namespace Finly.Models
{
    /// <summary>
    /// Encja DB (tabela Expenses).
    /// Uwaga: NIE trzymamy tu pól z JOINów (np. AccountName/Kind/CategoryName).
    /// To idzie do ExpenseDisplayModel.
    /// </summary>
    public class Expense
    {
        public int Id { get; set; }

        public double Amount { get; set; }


        public int CategoryId { get; set; }

        public DateTime Date { get; set; } = DateTime.Today;

        public string Description { get; set; } = string.Empty;

        public string Account { get; set; } = "";

        public int UserId { get; set; }

        public bool IsPlanned { get; set; } = false;

        public int? BudgetId { get; set; }

        public PaymentKind PaymentKind { get; set; } = PaymentKind.FreeCash;

        /// <summary>
        /// Id referencji zale¿nie od PaymentKind:
        /// - BankAccount => BankAccountId
        /// - Envelope    => EnvelopeId
        /// - FreeCash/SavedCash => null
        /// </summary>
        public int? PaymentRefId { get; set; }

        /// <summary>
        /// Powi¹zanie z kredytem (jeœli wydatek jest rat¹ / op³at¹ kredytu).
        /// </summary>
        public int? LoanId { get; set; }

        /// <summary>
        /// Powi¹zanie z konkretn¹ rat¹ w harmonogramie (jeœli stosujesz).
        /// </summary>
        public int? LoanInstallmentId { get; set; }

        public override string ToString()
            => $"{Date:yyyy-MM-dd} | CategoryId={CategoryId} | {Amount:0.##} | {Description}";
    }
}
