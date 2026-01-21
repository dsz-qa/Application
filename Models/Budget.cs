using System;

namespace Finly.Models
{
    public class Budget
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal PlannedAmount { get; set; }
        public override string ToString()
        {
            return Name ?? base.ToString();
        }
    }
}