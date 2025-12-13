using System;
using System.Data;
// using System.Data.SQLite; // removed to avoid missing reference; we use DatabaseService.GetConnection()
using Finly.ViewModels;
using System.Collections.Generic;
using Finly.Services.Features;

namespace Finly.Services.SpecificPages
{
    public static class ReportsService
    {
        public sealed class ReportItem
        {
            public DateTime Date { get; set; }
            public string Category { get; set; } = "";
            public decimal Amount { get; set; }
            public string Account { get; set; } = "";
            public string Type { get; set; } = ""; // "Wydatek" / "Przychód"
        }

        public static string BuildDefaultReportFileName(DateTime fromDate, DateTime toDate)
        {
            return $"Finly_Raport_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf";
        }

        public static DataTable LoadExpensesReport(int uid, DateTime from, DateTime to,
            ReportsViewModel.SourceType source, string selectedCategory,
            Models.BankAccountModel? selectedBankAccount, string selectedEnvelope)
        {
            int? accountId = source == ReportsViewModel.SourceType.BankAccounts && selectedBankAccount != null && selectedBankAccount.Id > 0
                ? selectedBankAccount.Id
                : null;

            return DatabaseService.GetExpenses(uid, from, to,
                GetCategoryIdSafe(uid, selectedCategory), null, accountId);
        }

        public static DataTable LoadIncomesReport(int uid, DateTime from, DateTime to,
            ReportsViewModel.SourceType source, string selectedCategory,
            Models.BankAccountModel? selectedBankAccount, string selectedEnvelope)
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
            Models.BankAccountModel? selectedBankAccount, string selectedEnvelope)
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

        public static List<ReportItem> LoadReport(
            int userId,
            string source,
            string category,
            string transactionType,
            string moneyPlace,
            DateTime from,
            DateTime to)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();

            // Budujemy osobne SELECT-y dla wydatków i przychodów.
            // Uwaga: NIE używamy już e.Source / i.Source, bo takich kolumn nie ma.
            var parts = new List<string>();

            if (transactionType == "Wydatki" || transactionType == "Wszystko")
            {
                parts.Add(@"
SELECT 
    e.Date                                   AS TxDate,
    'Wydatek'                                AS TxType,
    COALESCE(c.Name,'(brak kategorii)')      AS CategoryName,
    ''                                       AS AccountName,
    e.Amount * -1                            AS Amount
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId = @uid
  AND IFNULL(e.IsPlanned,0) = 0
  AND e.Date >= @from AND e.Date <= @to
");
            }

            if (transactionType == "Przychody" || transactionType == "Wszystko")
            {
                parts.Add(@"
SELECT 
    i.Date                                   AS TxDate,
    'Przychód'                               AS TxType,
    COALESCE(c.Name,'(brak kategorii)')      AS CategoryName,
    ''                                       AS AccountName,
    i.Amount                                 AS Amount
FROM Incomes i
LEFT JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId = @uid
  AND IFNULL(i.IsPlanned,0) = 0
  AND i.Date >= @from AND i.Date <= @to
");
            }

            if (parts.Count == 0)
                return new List<ReportItem>();

            var innerSql = string.Join("\nUNION ALL\n", parts);

            // Zewnętrzne SELECT + proste filtry po kategorii.
            var sql = $@"
SELECT *
FROM (
    {innerSql}
) t
WHERE 1=1
";

            if (!string.IsNullOrWhiteSpace(category) && category != "Wszystkie kategorie")
            {
                sql += " AND t.CategoryName = @cat";
                cmd.Parameters.AddWithValue("@cat", category);
            }

            // Docelowo można dodać filtr po source / moneyPlace, kiedy AccountName będzie realnie wyliczany.

            sql += " ORDER BY t.TxDate;";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

            var result = new List<ReportItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var item = new ReportItem
                {
                    Date     = DateTime.Parse(r["TxDate"].ToString() ?? DateTime.MinValue.ToString("yyyy-MM-dd")),
                    Category = r["CategoryName"]?.ToString() ?? "(brak kategorii)",
                    Amount   = Convert.ToDecimal(r["Amount"]),
                    Account  = r["AccountName"]?.ToString() ?? "",
                    Type     = r["TxType"]?.ToString() ?? ""
                };
                result.Add(item);
            }

            return result;
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
