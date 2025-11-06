using System;

namespace Finly.Services
{
    public enum RangePreset
    {
        Brak = 0,
        Dzisiaj,
        TenTydzien,
        TenMiesiac,
        TenRok
    }

    public static class DateRangeService
    {
        /// <summary>
        /// Zwraca [from, to] dla wybranego presetu. Tydzień liczony od poniedziałku do niedzieli.
        /// </summary>
        public static void GetRange(RangePreset preset, out DateTime? from, out DateTime? to)
        {
            var today = DateTime.Today;

            switch (preset)
            {
                case RangePreset.Dzisiaj:
                    from = today;
                    to = today;
                    return;

                case RangePreset.TenTydzien:
                    int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var monday = today.AddDays(-diff);
                    var sunday = monday.AddDays(6);
                    from = monday;
                    to = sunday;
                    return;

                case RangePreset.TenMiesiac:
                    var first = new DateTime(today.Year, today.Month, 1);
                    var last = first.AddMonths(1).AddDays(-1);
                    from = first;
                    to = last;
                    return;

                case RangePreset.TenRok:
                    var start = new DateTime(today.Year, 1, 1);
                    var end = new DateTime(today.Year, 12, 31);
                    from = start;
                    to = end;
                    return;

                default:
                    from = null;
                    to = null;
                    return;
            }
        }
    }
}
