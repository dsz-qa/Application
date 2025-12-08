using System;

namespace Finly.Services
{
    /// <summary>
    /// Pomocniczy serwis raportów – logika niezwiązana bezpośrednio z UI.
    /// Na razie dostarcza m.in. spójną nazwę pliku PDF dla raportu.
    /// </summary>
    public static class ReportsService
    {
        /// <summary>
        /// Buduje domyślną nazwę pliku raportu PDF na podstawie zakresu dat.
        /// Przykład: Finly_Raport_20250101_20250131.pdf
        /// </summary>
        public static string BuildDefaultReportFileName(DateTime fromDate, DateTime toDate)
        {
            return $"Finly_Raport_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf";
        }
    }
}
