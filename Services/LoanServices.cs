using System;
using System.Collections.Generic;

namespace Finly.Services
{
    /// <summary>
    /// Logika kredytów: wyliczanie rat, odsetek dziennych, harmonogramu.
    /// </summary>
    public static class LoanService
    {
        private const int DaysInYear = 365; // uproszczenie – wystarczy do analiz

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
        /// Rozbij pierwszą ratę na część odsetkową i kapitałową.
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
        /// Odsetki dzienne w uproszczeniu: prosto po liczbie dni.
        /// </summary>
        public static decimal CalculateDailyInterest(
            decimal principal,
            decimal annualRatePercent,
            DateTime fromDate,
            DateTime toDate)
        {
            if (principal <= 0m || annualRatePercent <= 0m)
                return 0m;

            if (toDate <= fromDate)
                return 0m;

            var days = (toDate - fromDate).Days;
            var dailyRate = annualRatePercent / 100m / DaysInYear;

            var interest = principal * dailyRate * days;
            return Math.Round(interest, 2);
        }

        /// <summary>
        /// Poprzedni „umowny” termin raty względem podanej daty.
        /// Używamy go do liczenia odsetek między ratą a nadpłatą.
        /// </summary>
        public static DateTime GetPreviousDueDate(DateTime today, int paymentDay)
        {
            if (paymentDay <= 0)
                return today.Date;

            // termin w bieżącym miesiącu
            var thisMonthDays = DateTime.DaysInMonth(today.Year, today.Month);
            var thisDay = Math.Min(paymentDay, thisMonthDays);
            var thisDue = new DateTime(today.Year, today.Month, thisDay);

            if (today.Date >= thisDue.Date)
                return thisDue;

            // wstecz – miesiąc wcześniej
            var prevMonthFirst = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            var prevMonthDays = DateTime.DaysInMonth(prevMonthFirst.Year, prevMonthFirst.Month);
            var prevDay = Math.Min(paymentDay, prevMonthDays);
            return new DateTime(prevMonthFirst.Year, prevMonthFirst.Month, prevDay);
        }

        /// <summary>
        /// Prosty harmonogram równych rat – bez nadpłat.
        /// </summary>
        public static List<LoanScheduleRow> GenerateSimpleSchedule(
            decimal principal,
            decimal annualRatePercent,
            int termMonths,
            DateTime startDate,
            int paymentDay)
        {
            var result = new List<LoanScheduleRow>();
            if (principal <= 0m || termMonths <= 0)
                return result;

            var payment = CalculateMonthlyPayment(principal, annualRatePercent, termMonths);
            var monthlyRate = annualRatePercent / 100m / 12m;
            var remaining = principal;
            var dueDate = startDate;

            for (int i = 1; i <= termMonths; i++)
            {
                // kolejne terminy liczymy na podstawie dnia płatności
                dueDate = GetNextDueDate(dueDate, paymentDay);

                var interest = Math.Round(remaining * monthlyRate, 2);
                var capital = Math.Round(payment - interest, 2);
                if (capital < 0m) capital = 0m;

                if (capital > remaining)
                {
                    capital = remaining;
                    payment = capital + interest;
                }

                remaining = Math.Max(0m, remaining - capital);

                result.Add(new LoanScheduleRow
                {
                    DueDate = dueDate,
                    Total = payment,
                    PrincipalPart = capital,
                    InterestPart = interest,
                    RemainingPrincipal = remaining
                });
            }

            return result;
        }

        /// <summary>
        /// Kolejny termin raty – płatności 15-tego z korektą weekendową.
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

    public class LoanScheduleRow
    {
        public DateTime DueDate { get; set; }
        public decimal Total { get; set; }
        public decimal PrincipalPart { get; set; }
        public decimal InterestPart { get; set; }
        public decimal RemainingPrincipal { get; set; }

        public string Display =>
            $"{DueDate:dd.MM.yyyy} — {Total:N2} zł (kapitał {PrincipalPart:N2}, odsetki {InterestPart:N2})";
    }
}
