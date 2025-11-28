using System;

namespace Finly.Models
{
    public class Expense
    {
        public int Id { get; set; }

        // Zostawiamy double (masz ju¿ double.TryParse w kodzie UI)
        public double Amount { get; set; }

        public int CategoryId { get; set; }

        // Tekstowa nazwa kategorii (bezpieczna pod null)
        public string CategoryName { get; set; } = string.Empty;

        public DateTime Date { get; set; } = DateTime.Today;

        public string Description { get; set; } = string.Empty;

        public int UserId { get; set; }

        // Czy transakcja jest zaplanowana (nie wp³ywa na salda)
        public bool IsPlanned { get; set; } = false;

        // Nowe: konto/Ÿród³o p³atnoœci (karta/gotówka/bank)
        public string Account { get; set; } = string.Empty;

        // Nowe: rodzaj transakcji (Wydatek/Przychód/Transfer)
        public string Kind { get; set; } = string.Empty;

        // Wygodne w³aœciwoœci formatuj¹ce do bindowania
        public string DateDisplay => Date.ToString("yyyy-MM-dd");
        public string AmountStr => Amount.ToString("N2") + " z³";

        // Alias zgodnoœci: czêœæ kodu mog³a u¿ywaæ 'Category'
        // => wskazuje na CategoryName, wiêc nic siê nie rozsypie.
        public string Category
        {
            get => CategoryName;
            set => CategoryName = value ?? string.Empty;
        }

        public override string ToString()
            => $"{Date:yyyy-MM-dd} | {CategoryName} | {Amount:0.##} | {Description}";
    }
}
