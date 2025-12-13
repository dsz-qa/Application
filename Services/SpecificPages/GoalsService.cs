using System;
using System.Collections.ObjectModel;
using System.Linq;
using Finly.Pages;

namespace Finly.Services.SpecificPages
{
    /// <summary>
    /// Prosty „magazyn” celów – współdzielony między stronami.
    /// </summary>
    public static class GoalsService
    {
        public static ObservableCollection<GoalVm> Goals { get; } = new();

        /// <summary>
        /// Wywołuj z Kopert – na podstawie tekstu z pola Cel.
        /// </summary>
        public static void AddOrUpdateFromEnvelope(string goalName, decimal target, decimal allocated)
        {
            if (string.IsNullOrWhiteSpace(goalName)) return;

            var name = goalName.Trim();

            var existing = Goals.FirstOrDefault(g =>
                string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                Goals.Add(new GoalVm
                {
                    Name = name,
                    TargetAmount = target,
                    CurrentAmount = allocated
                    // Termin i opis użytkownik może ustawić już na zakładce „Cele”
                });
            }
            else
            {
                existing.TargetAmount = target;
                existing.CurrentAmount = allocated;
            }
        }
    }
}

