namespace Finly.Models
{
    public class InvestmentModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public string? TargetDate { get; set; } // stored as YYYY-MM-DD
        public string? Description { get; set; }
    }
}
