using System;

namespace Finly.Models
{
    // Stabilnie: enum poza klas¹ (³atwiejsze u¿ycie w ca³ym projekcie)
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

        // Zostawiamy double (tak masz w UI i DB)
        public double Amount { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public DateTime Date { get; set; } = DateTime.Today;

        public string Description { get; set; } = string.Empty;

        public int UserId { get; set; }

        // Nie wp³ywa na salda
        public bool IsPlanned { get; set; } = false;

        // Tekst do UI / wsteczna zgodnoœæ (nie do ksiêgowania!)
        public string Account { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public int? BudgetId { get; set; }

        // NOWE – Ÿród³o p³atnoœci (to jest jedyna prawda do ksiêgowania)
        public PaymentKind PaymentKind { get; set; } = PaymentKind.FreeCash;

        // BankAccountId albo EnvelopeId; dla cash null
        public int? PaymentRefId { get; set; }

        // Formatowanie do bindowania
        public string DateDisplay => Date.ToString("yyyy-MM-dd");
        public string AmountStr => Amount.ToString("N2") + " z³";

        // Alias zgodnoœci
        public string Category
        {
            get => CategoryName;
            set => CategoryName = value ?? string.Empty;
        }

        public override string ToString()
            => $"{Date:yyyy-MM-dd} | {CategoryName} | {Amount:0.##} | {Description}";
    }
}
