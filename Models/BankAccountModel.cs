using System;

namespace Finly.Models
{
    /// Prosty model konta bankowego (lokalne konto w aplikacji)
    public class BankAccountModel
    {
        public int Id { get; set; }
        public int? ConnectionId { get; set; }   // opcjonalne (pod przyszłe open-banking)
        public int UserId { get; set; }

        public string BankName { get; set; } = "";   // opcjonalnie używane
        public string AccountName { get; set; } = "";
        public string Iban { get; set; } = "";
        public string Currency { get; set; } = "PLN";
        public decimal Balance { get; set; } = 0m;

        public DateTime? LastSync { get; set; }  // dla przyszłej synchronizacji

        public override string ToString() => string.IsNullOrWhiteSpace(AccountName) ? $"Konto #{Id}" : AccountName;
    }
}
