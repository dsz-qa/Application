using System;
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
            public string Type { get; set; } = ""; // "Wydatek" / "Przychód" / "Transfer"
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

            // source / moneyPlace do rozbudowy później (jak będziesz realnie wyliczać AccountName)

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
                    Date = DateTime.Parse(r["TxDate"].ToString() ?? DateTime.MinValue.ToString("yyyy-MM-dd")),
                    Category = r["CategoryName"]?.ToString() ?? "(brak kategorii)",
                    Amount = Convert.ToDecimal(r["Amount"]),
                    Account = r["AccountName"]?.ToString() ?? "",
                    Type = r["TxType"]?.ToString() ?? ""
                };
                result.Add(item);
            }

            return result;
        }
    }
}
