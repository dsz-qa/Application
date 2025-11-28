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

        // Nowe: konto/Ÿród³o u¿ywane do p³atnoœci/odbioru
        public string Account { get; set; } = string.Empty;

        // Nowe: rodzaj transakcji (Wydatek/Przychód/Transfer)
        public string Kind { get; set; } = string.Empty;

        // Wygodne pola do bindowania w tabeli (opcjonalne)
        public string DateDisplay => Date.ToString("d");
        public string AmountDisplay => Amount.ToString("N2") + " z³";
        public string AmountStr => AmountDisplay;
    }
}
