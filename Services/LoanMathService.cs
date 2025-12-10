using System;

namespace Finly.Services
{
    /// <summary>
    /// Pomocniczy serwis matematyczny do obliczeń kredytowych:
    /// - odsetki dzienne,
    /// - symulacje,
    /// - przeliczenia rat.
    /// </summary>
    public static class LoanMathService
    {
        /// <summary>
        /// Oblicza odsetki należne między dwiema datami wg oprocentowania rocznego.
        /// Metoda liczy jak banki: odsetki = kapitał * (oprocentowanie/365) * liczba dni.
        /// </summary>
        public static decimal CalculateInterest(
            decimal principal,
            decimal annualRate,
            DateTime from,
            DateTime to)
        {
            if (to <= from || principal <= 0 || annualRate <= 0)
                return 0m;

            int days = (to.Date - from.Date).Days;

            // Oprocentowanie dzienne wg zasady: nominal / 365
            decimal dailyRate = annualRate / 100m / 365m;

            return Math.Round(principal * dailyRate * days, 2);
        }
    }
}
