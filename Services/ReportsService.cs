using System;
using System.Data;
// using System.Data.SQLite; // removed to avoid missing reference; we use DatabaseService.GetConnection()
using Finly.ViewModels;

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

        public static DataTable LoadExpensesReport(int uid, DateTime from, DateTime to,
            ReportsViewModel.SourceType source, string selectedCategory,
            Finly.Models.BankAccountModel? selectedBankAccount, string selectedEnvelope)
        {
            // Reuse existing DatabaseService.GetExpenses with optional filters
            int? accountId = (source == ReportsViewModel.SourceType.BankAccounts && selectedBankAccount != null && selectedBankAccount.Id > 0)
                ? selectedBankAccount.Id
                : (int?)null;

            return DatabaseService.GetExpenses(uid, from, to,
                GetCategoryIdSafe(uid, selectedCategory), null, accountId);
        }

        public static DataTable LoadIncomesReport(int uid, DateTime from, DateTime to,
            ReportsViewModel.SourceType source, string selectedCategory,
            Finly.Models.BankAccountModel? selectedBankAccount, string selectedEnvelope)
        {
            var dt = new DataTable();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("Date", typeof(string));
            dt.Columns.Add("Amount", typeof(decimal));
            dt.Columns.Add("Description", typeof(string));
            dt.Columns.Add("CategoryName", typeof(string));

            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
SELECT i.Id, i.Date, i.Amount, i.Description,
       COALESCE(c.Name, '(brak)') AS CategoryName
FROM Incomes i
LEFT JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId=@u AND i.Date>=@from AND i.Date<=@to";
                cmd.Parameters.AddWithValue("@u", uid);
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var row = dt.NewRow();
                    row["Id"] = r.GetInt32(0);
                    row["Date"] = r.GetString(1);
                    row["Amount"] = Convert.ToDecimal(r.GetValue(2));
                    row["Description"] = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                    row["CategoryName"] = r.IsDBNull(4) ? "(brak)" : r.GetString(4);
                    dt.Rows.Add(row);
                }
            }
            catch { }

            return dt;
        }

        public static DataTable LoadAllTransactionsReport(int uid, DateTime from, DateTime to,
            ReportsViewModel.SourceType source, string selectedCategory,
            Finly.Models.BankAccountModel? selectedBankAccount, string selectedEnvelope)
        {
            var dt = new DataTable();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("Date", typeof(string));
            dt.Columns.Add("Amount", typeof(decimal));
            dt.Columns.Add("Description", typeof(string));
            dt.Columns.Add("CategoryName", typeof(string));
            dt.Columns.Add("Type", typeof(string));

            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
SELECT e.Id, e.Date, e.Amount, e.Description,
       COALESCE(ec.Name, '(brak)') AS CategoryName,
       'Wydatek' AS Type
FROM Expenses e
LEFT JOIN Categories ec ON ec.Id = e.CategoryId
WHERE e.UserId=@u AND e.Date>=@from AND e.Date<=@to
UNION ALL
SELECT i.Id, i.Date, i.Amount, i.Description,
       COALESCE(ic.Name, '(brak)') AS CategoryName,
       'Przychód' AS Type
FROM Incomes i
LEFT JOIN Categories ic ON ic.Id = i.CategoryId
WHERE i.UserId=@u AND i.Date>=@from AND i.Date<=@to
ORDER BY Date ASC";
                cmd.Parameters.AddWithValue("@u", uid);
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var row = dt.NewRow();
                    row["Id"] = r.GetInt32(0);
                    row["Date"] = r.GetString(1);
                    row["Amount"] = Convert.ToDecimal(r.GetValue(2));
                    row["Description"] = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                    row["CategoryName"] = r.IsDBNull(4) ? "(brak)" : r.GetString(4);
                    row["Type"] = r.GetString(5);
                    dt.Rows.Add(row);
                }
            }
            catch { }

            return dt;
        }

        private static int? GetCategoryIdSafe(int uid, string selectedCategory)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(selectedCategory) && selectedCategory != "Wszystkie kategorie")
                {
                    return DatabaseService.GetCategoryIdByName(uid, selectedCategory);
                }
            }
            catch { }
            return null;
        }
    }
}
