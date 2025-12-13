using System;

namespace Finly.Models
{
    public class Budget
    {
        public int Id { get; set; }

        /// <summary>Id zalogowanego użytkownika (tabela Users)</summary>
        public int UserId { get; set; }

        /// <summary>Nazwa budżetu, np. "Budżet domowy"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Typ: np. "Wydatek", "Przychód" – tak jak masz w bazie</summary>
        public string Type { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>Kwota zaplanowana</summary>
        public decimal PlannedAmount { get; set; }

        // (opcjonalnie – jeśli później będziesz tego używać)
        // public decimal SpentAmount   { get; set; }

        public override string ToString()
        {
            // Dzięki temu wszędzie (np. w ComboBoxach) zamiast
            // "Finly.Models.Budget" zobaczysz po prostu nazwę budżetu
            return Name ?? base.ToString();
        }
    }
}