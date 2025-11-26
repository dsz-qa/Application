using System;

namespace Finly.Pages
{
    public sealed class LoanCardVm
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public decimal Principal { get; set; }
        public decimal InterestRate { get; set; }
        public DateTime StartDate { get; set; }
        public int TermMonths { get; set; }
        public int PaymentDay { get; set; } = 0; // 0 = unspecified

        public string PrincipalStr => Principal.ToString("N0") + " z³";

        public double PercentPaidClamped
        {
            get
            {
                if (Principal <= 0) return 100.0;
                return 0.0;
            }
        }

        // Compute next payment date taking PaymentDay into account when set
        public DateTime NextPaymentDate
        {
            get
            {
                var today = DateTime.Today;
                if (PaymentDay <= 0)
                {
                    // fallback: one month after start
                    var fallback = StartDate.AddMonths(1);
                    if (fallback < today) fallback = today.AddMonths(1);
                    return fallback;
                }

                // Determine next date with given day-of-month
                DateTime candidateMonth = new DateTime(today.Year, today.Month, 1);
                // Try this month
                int daysInThisMonth = DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month);
                int day = Math.Min(PaymentDay, daysInThisMonth);
                var candidate = new DateTime(candidateMonth.Year, candidateMonth.Month, day);
                if (candidate <= today)
                {
                    // move to next month
                    candidateMonth = candidateMonth.AddMonths(1);
                    int dim = DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month);
                    day = Math.Min(PaymentDay, dim);
                    candidate = new DateTime(candidateMonth.Year, candidateMonth.Month, day);
                }
                return candidate;
            }
        }

        public decimal NextPayment => Math.Round(Principal > 0 ? Principal / Math.Max(1, TermMonths) : 0m, 0);
        public string NextPaymentInfo => NextPayment.ToString("N0") + " z³ · " + NextPaymentDate.ToString("dd.MM.yyyy");

        public string RemainingTermStr
        {
            get
            {
                if (TermMonths <= 0) return "—";
                var monthsLeft = Math.Max(0, TermMonths - ((DateTime.Today.Year - StartDate.Year) * 12 + DateTime.Today.Month - StartDate.Month));
                var years = monthsLeft / 12;
                var months = monthsLeft % 12;
                return $"{years} lat {months} mies.";
            }
        }
    }
}
