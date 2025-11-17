namespace Finly.Models
{
    public class CategorySummary
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = "";
        public string TypeDisplay { get; set; } = "";
        public int EntryCount { get; set; }
        public decimal TotalAmount { get; set; }
        public double SharePercent { get; set; }
    }
}
