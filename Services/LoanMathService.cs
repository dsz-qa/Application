using System;

namespace Finly.Services
{
    /// <summary>
    /// Pomocniczy serwis matematyczny do obliczeń kredytowych.
    /// Jedno źródło prawdy dla odsetek dziennych.
    /// </summary>
    public static class LoanMathService
    {
        /// <summary>
        /// Odsetki: kapitał * (oprocentowanie/365) * liczba dni.
        /// annualRate podajesz w % (np. 6.28).
        /// </summary>
        public static decimal CalculateInterest(
            decimal principal,
            decimal annualRate,
            DateTime from,
            DateTime to)
        {
            if (to <= from || principal <= 0m || annualRate <= 0m)
                return 0m;

            int days = (to.Date - from.Date).Days;
            if (days <= 0) return 0m;

            decimal dailyRate = annualRate / 100m / 365m;
            return Math.Round(principal * dailyRate * days, 2);
        }
    }
}
