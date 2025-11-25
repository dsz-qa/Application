// Minimalne typy zastępcze, które rozwiązują błędy kompilacji.
// Jeśli w projekcie masz własne implementacje, usuń poniższe deklaracje.

namespace Finly.Models
{
    internal enum BudgetType
    {
        Monthly,
        Weekly,
        Rollover,
        OneTime
    }

    internal class BudgetModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public BudgetType Type { get; set; } = BudgetType.Monthly;
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public decimal Amount { get; set; }
        public bool Active { get; set; } = true;
        public decimal LastRollover { get; set; } = 0m;
        public string LastPeriodKey { get; set; } = "";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}

namespace Finly.Services
{
    internal static class BudgetService
    {
        public static List<Finly.Models.BudgetModel>? LoadBudgets(int userId)
        {
            // Placeholder: zwróć pustą listę, zastąp prawdziwą implementacją
            return new List<Finly.Models.BudgetModel>();
        }

        public static void SaveBudgets(int userId, List<Finly.Models.BudgetModel> budgets)
        {
            // Placeholder: brak zapisu - zastąp implementacją zapisu
        }
    }
}
