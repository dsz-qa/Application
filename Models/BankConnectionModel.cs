using System;

namespace Finly.Models
{
    public class BankConnectionModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string BankName { get; set; } = "";
        public string AccountHolder { get; set; } = "";
        public string Status { get; set; } = "Połączono";
        public DateTime? LastSync { get; set; }
    }
}

