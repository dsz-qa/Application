using System;
using System.Collections.Generic;

namespace Finly.Services
{
    /// <summary>
    /// Logika kredytów: wyliczanie rat annuitetowych i terminów płatności.
    /// Harmonogram CSV jest parsowany osobno w LoanScheduleCsvParser.
    /// </summary>
    public static class LoanService
    {
        /// <summary>
        /// Standardowa rata annuitetowa.
        /// </summary>
        public static decimal CalculateMonthlyPayment(
            decimal principal,
            decimal annualRatePercent,
            int termMonths)
        {
            if (principal <= 0m || termMonths <= 0)
                return 0m;

            var r = annualRatePercent / 100m / 12m; // miesięczna stopa

            if (r == 0m)
                return Math.Round(principal / termMonths, 2);

            // A = P * r / (1 - (1+r)^-n)
            var denom = 1m - (decimal)Math.Pow((double)(1m + r), -termMonths);
            if (denom == 0m)
                return Math.Round(principal / termMonths, 2);

            var payment = principal * r / denom;
            return Math.Round(payment, 2);
        }

        /// <summary>
        /// Rozbija pierwszą ratę na część odsetkową i kapitałową.
        /// </summary>
        public static (decimal InterestPart, decimal PrincipalPart)
            CalculateFirstInstallmentBreakdown(
                decimal principal,
                decimal annualRatePercent,
                int termMonths)
        {
            var payment = CalculateMonthlyPayment(principal, annualRatePercent, termMonths);
            if (payment <= 0m)
                return (0m, 0m);

            var r = annualRatePercent / 100m / 12m;
            var interest = Math.Round(principal * r, 2);
            var capital = Math.Round(payment - interest, 2);
            if (capital < 0m) capital = 0m;

            return (interest, capital);
        }

        /// <summary>
        /// Poprzedni „umowny” termin raty względem podanej daty.
        /// Uwzględnia PaymentDay oraz start kredytu (nie cofamy się przed start).
        /// </summary>
        public static DateTime GetPreviousDueDate(DateTime today, int paymentDay, DateTime startDate)
        {
            if (paymentDay <= 0)
            {
                // przy braku PaymentDay liczymy "miesiąc wstecz", ale nie przed start
                var d = today.Date.AddMonths(-1);
                return d < startDate.Date ? startDate.Date : d;
            }

            int dim = DateTime.DaysInMonth(today.Year, today.Month);
            int day = Math.Min(paymentDay, dim);
            var thisDue = new DateTime(today.Year, today.Month, day);

            if (today.Date >= thisDue.Date)
            {
                return thisDue.Date < startDate.Date ? startDate.Date : thisDue.Date;
            }

            var prevMonthFirst = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            int dimPrev = DateTime.DaysInMonth(prevMonthFirst.Year, prevMonthFirst.Month);
            day = Math.Min(paymentDay, dimPrev);
            var prevDue = new DateTime(prevMonthFirst.Year, prevMonthFirst.Month, day);

            return prevDue.Date < startDate.Date ? startDate.Date : prevDue.Date;
        }

        /// <summary>
        /// Kolejny termin raty – płatności danego dnia miesiąca z korektą weekendową.
        /// </summary>
        public static DateTime GetNextDueDate(DateTime previousDueDate, int paymentDay)
        {
            if (paymentDay <= 0)
                return previousDueDate.AddMonths(1);

            // kolejny miesiąc
            var nextMonthFirst = new DateTime(previousDueDate.Year, previousDueDate.Month, 1).AddMonths(1);
            var daysInNextMonth = DateTime.DaysInMonth(nextMonthFirst.Year, nextMonthFirst.Month);
            var day = Math.Min(paymentDay, daysInNextMonth);

            var due = new DateTime(nextMonthFirst.Year, nextMonthFirst.Month, day);

            // weekend → na najbliższy dzień roboczy
            if (due.DayOfWeek == DayOfWeek.Saturday)
                due = due.AddDays(2);
            else if (due.DayOfWeek == DayOfWeek.Sunday)
                due = due.AddDays(1);

            return due;
        }
    }
}
