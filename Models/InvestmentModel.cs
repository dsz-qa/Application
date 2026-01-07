namespace Finly.Models
{
    public enum InvestmentType
    {
        Other = 0,
        Stock = 1,
        Bond = 2,
        Etf = 3,
        Fund = 4,
        Crypto = 5,
        Deposit = 6
    }

    public class InvestmentModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public string Name { get; set; } = "";

        public InvestmentType Type { get; set; } = InvestmentType.Other;

        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }

        public string? TargetDate { get; set; } // stored as YYYY-MM-DD
        public string? Description { get; set; }
    }
}
