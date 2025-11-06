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

        // G³ówne pole kategorii
        public string CategoryName
        {
            get => _categoryName;
            set => _categoryName = value ?? string.Empty;
        }

        // Alias zgodnoœci – zawsze wskazuje na CategoryName
        public string Category
        {
            get => _categoryName;
            set => _categoryName = value ?? string.Empty;
        }

        // Wygodne pola do bindowania w tabeli (opcjonalne)
        public string DateDisplay => Date.ToString("yyyy-MM-dd");
        public string AmountDisplay => Amount.ToString("0.##");
    }
}
