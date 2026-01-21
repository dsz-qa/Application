using System;

namespace Finly.Models
{
    public enum PaymentKind
    {
        FreeCash = 0,
        SavedCash = 1,
        BankAccount = 2,
        Envelope = 3
    }

    public class Expense
    {
        public int Id { get; set; }

        public double Amount { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public DateTime Date { get; set; } = DateTime.Today;

        public string Description { get; set; } = string.Empty;

        public int UserId { get; set; }

        public bool IsPlanned { get; set; } = false;

        public string Account { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public int? BudgetId { get; set; }

        public PaymentKind PaymentKind { get; set; } = PaymentKind.FreeCash;

        public int? PaymentRefId { get; set; }

        public string DateDisplay => Date.ToString("yyyy-MM-dd");
        public string AmountStr => Amount.ToString("N2") + " z³";

        public string Category
        {
            get => CategoryName;
            set => CategoryName = value ?? string.Empty;
        }

        public override string ToString()
            => $"{Date:yyyy-MM-dd} | {CategoryName} | {Amount:0.##} | {Description}";
    }
}
