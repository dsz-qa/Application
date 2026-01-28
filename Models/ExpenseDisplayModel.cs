using System;

namespace Finly.Models
{
    /// <summary>
    /// Model pod UI (wynik JOINów + pola do wyœwietlania).
    /// To NIE jest encja DB.
    /// </summary>
    public class ExpenseDisplayModel
    {
        // ====== rdzeñ jak Expense ======
        public int Id { get; set; }
        public double Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public string Description { get; set; } = string.Empty;
        public int UserId { get; set; }
        public bool IsPlanned { get; set; }

        public int CategoryId { get; set; }
        public int? BudgetId { get; set; }

        public PaymentKind PaymentKind { get; set; } = PaymentKind.FreeCash;
        public int? PaymentRefId { get; set; }

        public int? LoanId { get; set; }
        public int? LoanInstallmentId { get; set; }

        // ====== pola z JOINów / prezentacji ======
        private string _categoryName = string.Empty;
        public string CategoryName
        {
            get => _categoryName;
            set => _categoryName = value ?? string.Empty;
        }

        /// <summary>
        /// Nazwa portfela/konta/koperty do wyœwietlenia (np. "mBank", "Samochód").
        /// </summary>
        private string _paymentSourceName = string.Empty;
        public string PaymentSourceName
        {
            get => _paymentSourceName;
            set => _paymentSourceName = value ?? string.Empty;
        }

        // ====== helpery do UI ======
        public string DateDisplay => Date.ToString("yyyy-MM-dd");
        public string AmountDisplay => Amount.ToString("N2") + " z³";
        public string AmountStr => AmountDisplay;

        // alias (jeœli gdzieœ w UI masz binding do Category)
        public string Category
        {
            get => _categoryName;
            set => _categoryName = value ?? string.Empty;
        }
    }
}
