using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using Finly.Services.Features;

namespace Finly.Services.SpecificPages
{
    public static class ReportsService
    {
        public sealed class ReportItem
        {
            public DateTime Date { get; set; }

            // NOWE (wariant B): stabilne mapowanie po CategoryId
            public int? CategoryId { get; set; }

            public string Category { get; set; } = "";
            public decimal Amount { get; set; }          // Wydatek: ujemny, Przychód: dodatni, Transfer: dodatni
            public string Account { get; set; } = "";
            public string Type { get; set; } = "";      // "Wydatek" / "Przychód" / "Transfer"
        }

        private static readonly object _transferLock = new();
        private static bool _transferSqlInitialized;
        private static string? _cachedTransferSql;

        /// <summary>
        /// Ładuje raport transakcji w zadanym okresie.
        /// transactionType: "Wszystko" | "Wydatki" | "Przychody" | "Transfery"
        /// category: "Wszystkie kategorie" lub konkretna nazwa kategorii
        /// </summary>
        public static List<ReportItem> LoadReport(
            int userId,
            string category,
            string transactionType,
            DateTime from,
            DateTime to)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();

            // Normalizacja wejścia
            var type = (transactionType ?? "Wszystko").Trim();
            var cat = (category ?? "Wszystkie kategorie").Trim();

            var includeExpenses = type == "Wszystko" || type == "Wydatki";
            var includeIncomes = type == "Wszystko" || type == "Przychody";
            var includeTransfers = type == "Wszystko" || type == "Transfery";

            // Składamy UNION-y
            var parts = new List<string>();

            if (includeExpenses)
                parts.Add(BuildExpensesSql());

            if (includeIncomes)
                parts.Add(BuildIncomesSql());

            if (includeTransfers)
            {
                var transferSql = GetCachedTransfersSql(con);
                if (!string.IsNullOrWhiteSpace(transferSql))
                    parts.Add(transferSql);
            }

            if (parts.Count == 0)
                return new List<ReportItem>();

            // Główny SQL
            var innerSql = string.Join("\nUNION ALL\n", parts);

            var sql = $@"
SELECT *
FROM (
    {innerSql}
) t
WHERE 1=1
";

            // Filtrowanie kategorii (jak nie "Wszystkie kategorie")
            if (!string.IsNullOrWhiteSpace(cat) && cat != "Wszystkie kategorie")
            {
                sql += " AND t.CategoryName = @cat ";
                cmd.Parameters.AddWithValue("@cat", cat);
            }

            sql += " ORDER BY t.TxDate;";

            cmd.CommandText = sql;

            // parametry wspólne
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@from", from.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", to.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            // Czytanie
            var result = new List<ReportItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dateStr = r["TxDate"]?.ToString();
                _ = DateTime.TryParse(dateStr, out var dt);

                int? catId = null;
                try
                {
                    if (r["CategoryId"] != DBNull.Value)
                        catId = Convert.ToInt32(r["CategoryId"], CultureInfo.InvariantCulture);
                }
                catch
                {
                    catId = null;
                }

                result.Add(new ReportItem
                {
                    Date = dt == default ? DateTime.MinValue : dt,
                    Type = r["TxType"]?.ToString() ?? "",
                    CategoryId = catId,
                    Category = r["CategoryName"]?.ToString() ?? "(brak kategorii)",
                    Account = r["AccountName"]?.ToString() ?? "",
                    Amount = SafeDecimal(r["Amount"])
                });
            }

            return result;
        }

        private static string? GetCachedTransfersSql(IDbConnection con)
        {
            lock (_transferLock)
            {
                if (_transferSqlInitialized)
                    return _cachedTransferSql;

                _cachedTransferSql = TryBuildTransfersSql(con);
                _transferSqlInitialized = true;
                return _cachedTransferSql;
            }
        }

        // =========================
        // SQL – Expenses / Incomes
        // =========================

        private static string BuildExpensesSql() => @"
SELECT 
    e.Date                                   AS TxDate,
    'Wydatek'                                AS TxType,
    c.Id                                     AS CategoryId,
    COALESCE(c.Name,'(brak kategorii)')      AS CategoryName,
    ''                                       AS AccountName,
    (ABS(e.Amount) * -1)                     AS Amount
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId = @uid
  AND IFNULL(e.IsPlanned,0) = 0
  AND DATE(e.Date) >= DATE(@from) AND DATE(e.Date) <= DATE(@to)
";

        private static string BuildIncomesSql() => @"
SELECT 
    i.Date                                   AS TxDate,
    'Przychód'                               AS TxType,
    c.Id                                     AS CategoryId,
    COALESCE(c.Name,'(brak kategorii)')      AS CategoryName,
    ''                                       AS AccountName,
    ABS(i.Amount)                            AS Amount
FROM Incomes i
LEFT JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId = @uid
  AND IFNULL(i.IsPlanned,0) = 0
  AND DATE(i.Date) >= DATE(@from) AND DATE(i.Date) <= DATE(@to)
";

        // =========================
        // Transfery – best effort
        // =========================
        private static string? TryBuildTransfersSql(IDbConnection con)
        {
            // typowe nazwy tabel spotykane w apkach finansowych
            var candidates = new[]
            {
                "Transfers",
                "AccountTransfers",
                "BankTransfers",
                "MoneyTransfers",
                "InternalTransfers",
                "Transactions"
            };

            var existing = GetExistingTables(con);

            foreach (var table in candidates)
            {
                if (!existing.Contains(table, StringComparer.OrdinalIgnoreCase))
                    continue;

                var cols = GetTableColumns(con, table);

                if (!HasCol(cols, "Date") || !HasCol(cols, "Amount") || !HasCol(cols, "UserId"))
                    continue;

                var hasIsPlanned = HasCol(cols, "IsPlanned");

                // UWAGA: Twoja tabela Transfers nie ma CategoryId, więc zwykle to będzie false.
                var hasCategoryId = HasCol(cols, "CategoryId");

                var isPlannedFilter = hasIsPlanned ? "AND IFNULL(t.IsPlanned,0) = 0" : "";

                var categoryJoin = hasCategoryId
                    ? "LEFT JOIN Categories c ON c.Id = t.CategoryId"
                    : "";

                var categoryIdSelect = hasCategoryId
                    ? "c.Id"
                    : "NULL";

                var categoryNameSelect = hasCategoryId
                    ? "COALESCE(c.Name,'(brak kategorii)')"
                    : "'(brak kategorii)'";

                var accountExpr = BuildTransferAccountExpr(cols);

                return $@"
SELECT
    t.Date                      AS TxDate,
    'Transfer'                  AS TxType,
    {categoryIdSelect}          AS CategoryId,
    {categoryNameSelect}        AS CategoryName,
    {accountExpr}               AS AccountName,
    ABS(t.Amount)               AS Amount
FROM {table} t
{categoryJoin}
WHERE t.UserId = @uid
  {isPlannedFilter}
  AND DATE(t.Date) >= DATE(@from) AND DATE(t.Date) <= DATE(@to)
";
            }

            return null;
        }

        private static string BuildTransferAccountExpr(HashSet<string> cols)
        {
            var fromName = FirstExisting(cols, "FromAccountName", "FromName", "SourceAccountName");
            var toName = FirstExisting(cols, "ToAccountName", "ToName", "TargetAccountName");

            if (!string.IsNullOrWhiteSpace(fromName) && !string.IsNullOrWhiteSpace(toName))
                return $"(COALESCE(t.{fromName},'') || ' → ' || COALESCE(t.{toName},''))";

            return "''";
        }

        // =========================
        // SQLite helpers
        // =========================

        private static HashSet<string> GetExistingTables(IDbConnection con)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT name
FROM sqlite_master
WHERE type='table'
";
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
            }
            return set;
        }

        private static HashSet<string> GetTableColumns(IDbConnection con, string table)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{table}');";

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
            }
            return set;
        }

        private static bool HasCol(HashSet<string> cols, string name)
            => cols.Contains(name);

        private static string? FirstExisting(HashSet<string> cols, params string[] names)
        {
            foreach (var n in names)
                if (cols.Contains(n))
                    return n;
            return null;
        }

        private static decimal SafeDecimal(object? v)
        {
            try
            {
                if (v == null || v == DBNull.Value) return 0m;
                return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0m;
            }
        }
    }
}
