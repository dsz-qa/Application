using System;

namespace Finly.Views.Dialogs
{
    public class BudgetDialogViewModel
    {
        public string Name { get; set; } = string.Empty;

        // "Wydatek" / "Przychód"
        public string Type { get; set; } = "Wydatek";

        public string TypeDisplay { get; set; } = string.Empty;

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public decimal PlannedAmount { get; set; }

        // >>> NOWE POLE – aktualnie wydana kwota w budżecie
        public decimal SpentAmount { get; set; }
    }
}