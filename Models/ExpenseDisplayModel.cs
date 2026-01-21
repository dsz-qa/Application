using System;

namespace Finly.Models
{
    public class ExpenseDisplayModel
    {
        public int Id { get; set; }
        public double Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public string Description { get; set; } = string.Empty;
        public int UserId { get; set; }

        private string _categoryName = string.Empty;
        public string CategoryName
        {
            get => _categoryName;
            set => _categoryName = value ?? string.Empty;
        }
        public string Category
        {
            get => _categoryName;
            set => _categoryName = value ?? string.Empty;
        }

        public string Account { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string DateDisplay => Date.ToString("d");
        public string AmountDisplay => Amount.ToString("N2") + " z³";
        public string AmountStr => AmountDisplay;
    }
}
