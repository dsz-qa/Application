using System;

namespace Finly.Models
{
    public class BankAccountModel
    {
        public int Id { get; set; }
        public int ConnectionId { get; set; }
        public int UserId { get; set; }

        public string BankName { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string Iban { get; set; } = "";
        public string Currency { get; set; } = "PLN";
        public decimal Balance { get; set; }
        public DateTime? LastSync { get; set; }
    }
}
