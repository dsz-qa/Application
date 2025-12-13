using Finly.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Finly.Services
{
    /// <summary>
    /// Centralny serwis SQLite:
    /// - po³¹czenie, schemat i migracje,
    /// - CRUD i zapytania,
    /// - event DataChanged dla UI.
    ///
    /// Uwaga: ksiêgowanie sald (bank/cash/saved/envelopes) jest w LedgerService.
    /// </summary>
    public static class DatabaseService
    {
        // ====== stan/lock schematu ======
        private static readonly object _schemaLock = new();
        private static bool _schemaInitialized = false;

        // ====== œcie¿ka bazy ======
        private static string DbPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Finly");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "finly.db");
            }
        }

        // ====== event dla UI ======
        public static event EventHandler? DataChanged;

        private static void RaiseDataChanged()
        {
            try { DataChanged?.Invoke(null, EventArgs.Empty); }
            catch { }
        }

        /// <summary>
        /// Umo¿liwia innym serwisom (LedgerService) odpalenie eventu bez duplikacji logiki.
        /// </summary>
        internal static void NotifyDataChanged() => RaiseDataChanged();

        // ====== po³¹czenia ======
        public static SqliteConnection GetConnection()
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default
            }.ToString();

            var con = new SqliteConnection(cs);
            con.Open();

            using var pragma = con.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();

            return con;
        }

        /// <summary>
        /// Zapewnia, ¿e schemat i migracje zosta³y wykonane.
        /// </summary>
        public static void EnsureTables()
        {
            lock (_schemaLock)
            {
                if (_schemaInitialized) return;

                using var c = GetConnection();

                // 1) g³ówny schemat (tabele bazowe)
                SchemaService.Ensure(c);

                // 2) dodatkowe migracje/kolumny wymagane przez UI
                EnsureBankAccountsSchema(c);
                EnsureExpensesSchema(c);
                EnsureBudgetsSchema(c);
                EnsureIncomesSchema(c);

                _schemaInitialized = true;
            }
        }

        private static SqliteConnection OpenAndEnsureSchema()
        {
            if (!_schemaInitialized)
            {
                lock (_schemaLock)
                {
                    if (!_schemaInitialized)
                    {
                        using var c0 = GetConnection();

                        SchemaService.Ensure(c0);

                        EnsureBankAccountsSchema(c0);
                        EnsureExpensesSchema(c0);
                        EnsureBudgetsSchema(c0);
                        EnsureIncomesSchema(c0);

                        _schemaInitialized = true;
                    }
                }
            }

            return GetConnection();
        }

        // =========================================================
        // ========================= HELPERS ========================
        // =========================================================

        private static string ToIsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string? GetNullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        private static string GetStringSafe(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

        private static DateTime GetDate(SqliteDataReader r, int i)
        {
            if (r.IsDBNull(i)) return DateTime.MinValue;
            var v = r.GetValue(i);
            if (v is DateTime dt) return dt;
            return DateTime.TryParse(v?.ToString(), out var p) ? p : DateTime.MinValue;
        }

        /// <summary>
        /// Sprawdza istnienie tabeli w bazie. (internal – u¿ywa LedgerService)
        /// </summary>
        internal static bool TableExists(SqliteConnection c, string tableName)
        {
            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1;";
                cmd.Parameters.AddWithValue("@n", tableName);
                var obj = cmd.ExecuteScalar();
                return obj != null && obj != DBNull.Value;
            }
            catch { return false; }
        }

        /// <summary>
        /// Sprawdza czy kolumna istnieje w tabeli. (internal – u¿ywa LedgerService)
        /// </summary>
        internal static bool ColumnExists(SqliteConnection c, string table, string column)
        {
            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info('{table}');";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var name = r.GetString(1);
                    if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool ExpensesHasAccountId(SqliteConnection c)
            => TableExists(c, "Expenses") && ColumnExists(c, "Expenses", "AccountId");

        private static bool ExpensesHasBudgetId(SqliteConnection c)
            => TableExists(c, "Expenses") && ColumnExists(c, "Expenses", "BudgetId");

        // =========================================================
        // ========================= MIGRACJE =======================
        // =========================================================

        private static void EnsureBankAccountsSchema(SqliteConnection c)
        {
            try
            {
                if (!TableExists(c, "BankAccounts")) return;

                if (!ColumnExists(c, "BankAccounts", "BankName"))
                {
                    using var alter = c.CreateCommand();
                    alter.CommandText = "ALTER TABLE BankAccounts ADD COLUMN BankName TEXT NULL;";
                    alter.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static void EnsureExpensesSchema(SqliteConnection c)
        {
            try
            {
                if (!TableExists(c, "Expenses")) return;

                if (!ColumnExists(c, "Expenses", "AccountId"))
                {
                    using var alter = c.CreateCommand();
                    alter.CommandText = "ALTER TABLE Expenses ADD COLUMN AccountId INTEGER NULL;";
                    alter.ExecuteNonQuery();
                }

                if (!ColumnExists(c, "Expenses", "BudgetId"))
                {
                    using var alterB = c.CreateCommand();
                    alterB.CommandText = "ALTER TABLE Expenses ADD COLUMN BudgetId INTEGER NULL;";
                    alterB.ExecuteNonQuery();
                }

                // NOWE kolumny dla stabilnego ksiêgowania
                if (!ColumnExists(c, "Expenses", "PaymentKind"))
                {
                    using var alterPk = c.CreateCommand();
                    alterPk.CommandText = "ALTER TABLE Expenses ADD COLUMN PaymentKind INTEGER NOT NULL DEFAULT 0;";
                    alterPk.ExecuteNonQuery();
                }

                if (!ColumnExists(c, "Expenses", "PaymentRefId"))
                {
                    using var alterPr = c.CreateCommand();
                    alterPr.CommandText = "ALTER TABLE Expenses ADD COLUMN PaymentRefId INTEGER NULL;";
                    alterPr.ExecuteNonQuery();
                }

                // Indeksy (bezpieczne)
                try
                {
                    using var idx1 = c.CreateCommand();
                    idx1.CommandText = "CREATE INDEX IF NOT EXISTS IX_Expenses_UserId_AccountId ON Expenses(UserId, AccountId);";
                    idx1.ExecuteNonQuery();
                }
                catch { }

                try
                {
                    using var idx2 = c.CreateCommand();
                    idx2.CommandText = "CREATE INDEX IF NOT EXISTS IX_Expenses_UserId_PaymentKind ON Expenses(UserId, PaymentKind);";
                    idx2.ExecuteNonQuery();
                }
                catch { }
            }
            catch { }
        }


        private static void EnsureBudgetsSchema(SqliteConnection c)
        {
            try
            {
                if (!TableExists(c, "Budgets")) return;

                if (!ColumnExists(c, "Budgets", "OverState"))
                {
                    using var alter = c.CreateCommand();
                    alter.CommandText = "ALTER TABLE Budgets ADD COLUMN OverState INTEGER NOT NULL DEFAULT 0;";
                    alter.ExecuteNonQuery();
                }

                if (!ColumnExists(c, "Budgets", "OverNotifiedAt"))
                {
                    using var alter = c.CreateCommand();
                    alter.CommandText = "ALTER TABLE Budgets ADD COLUMN OverNotifiedAt TEXT NULL;";
                    alter.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static void EnsureIncomesSchema(SqliteConnection c)
        {
            try
            {
                if (!TableExists(c, "Incomes")) return;

                if (!ColumnExists(c, "Incomes", "BudgetId"))
                {
                    using var alter = c.CreateCommand();
                    alter.CommandText = "ALTER TABLE Incomes ADD COLUMN BudgetId INTEGER NULL;";
                    alter.ExecuteNonQuery();
                }
            }
            catch { }
        }

        // =========================================================
        // ======================= USERS (CASCADE) ==================
        // =========================================================

        public static void DeleteUserCascade(int userId)
        {
            if (userId <= 0) return;

            using var con = OpenAndEnsureSchema();
            using var tx = con.BeginTransaction();
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;

            void Exec(string sql)
            {
                cmd.CommandText = sql;
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.ExecuteNonQuery();
            }

            bool Has(string tableName) => TableExists(con, tableName);

            if (Has("Incomes")) Exec("DELETE FROM Incomes WHERE UserId = @uid;");
            if (Has("Expenses")) Exec("DELETE FROM Expenses WHERE UserId = @uid;");
            if (Has("Transfers")) Exec("DELETE FROM Transfers WHERE UserId = @uid;");
            if (Has("Envelopes")) Exec("DELETE FROM Envelopes WHERE UserId = @uid;");
            if (Has("CashOnHand")) Exec("DELETE FROM CashOnHand WHERE UserId = @uid;");
            if (Has("SavedCash")) Exec("DELETE FROM SavedCash WHERE UserId = @uid;");
            if (Has("BankAccounts")) Exec("DELETE FROM BankAccounts WHERE UserId = @uid;");
            if (Has("BankConnections")) Exec("DELETE FROM BankConnections WHERE UserId = @uid;");
            if (Has("Categories")) Exec("DELETE FROM Categories WHERE UserId = @uid;");
            if (Has("Users")) Exec("DELETE FROM Users WHERE Id = @uid;");

            tx.Commit();
            RaiseDataChanged();
        }

        public static bool IsUserOnboarded(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IsOnboarded FROM Users WHERE Id=@id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", userId);
            var obj = cmd.ExecuteScalar();
            return obj != null && obj != DBNull.Value && Convert.ToInt32(obj) == 1;
        }

        public static void MarkUserOnboarded(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE Users SET IsOnboarded = 1 WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        // =========================================================
        // ======================= LOANS ============================
        // =========================================================

        public static List<LoanModel> GetLoans(int userId)
        {
            var list = new List<LoanModel>();
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT Id, UserId, Name, Principal, InterestRate, StartDate, TermMonths, Note, PaymentDay
FROM Loans
WHERE UserId=@u
ORDER BY Name;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new LoanModel
                {
                    Id = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    UserId = r.IsDBNull(1) ? userId : r.GetInt32(1),
                    Name = GetStringSafe(r, 2),
                    Principal = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
                    InterestRate = r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetValue(4)),
                    StartDate = GetDate(r, 5),
                    TermMonths = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    Note = GetNullableString(r, 7),
                    PaymentDay = r.IsDBNull(8) ? 0 : r.GetInt32(8)
                });
            }

            return list;
        }

        public static int InsertLoan(LoanModel loan)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Loans(UserId, Name, Principal, InterestRate, StartDate, TermMonths, Note, PaymentDay)
VALUES (@u, @n, @p, @ir, @d, @tm, @note, @pd);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", loan.UserId);
            cmd.Parameters.AddWithValue("@n", loan.Name ?? "");
            cmd.Parameters.AddWithValue("@p", loan.Principal);
            cmd.Parameters.AddWithValue("@ir", loan.InterestRate);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(loan.StartDate));
            cmd.Parameters.AddWithValue("@tm", loan.TermMonths);
            cmd.Parameters.AddWithValue("@note", (object?)loan.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pd", loan.PaymentDay);

            var id = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            RaiseDataChanged();
            return id;
        }

        public static void UpdateLoan(LoanModel loan)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE Loans
   SET Name = @n,
       Principal = @p,
       InterestRate = @ir,
       StartDate = @d,
       TermMonths = @tm,
       Note = @note,
       PaymentDay = @pd
 WHERE Id = @id AND UserId = @u;";
            cmd.Parameters.AddWithValue("@id", loan.Id);
            cmd.Parameters.AddWithValue("@u", loan.UserId);
            cmd.Parameters.AddWithValue("@n", loan.Name ?? "");
            cmd.Parameters.AddWithValue("@p", loan.Principal);
            cmd.Parameters.AddWithValue("@ir", loan.InterestRate);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(loan.StartDate));
            cmd.Parameters.AddWithValue("@tm", loan.TermMonths);
            cmd.Parameters.AddWithValue("@note", (object?)loan.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pd", loan.PaymentDay);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void DeleteLoan(int id, int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Loans WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        // =========================================================
        // ======================= CATEGORIES =======================
        // =========================================================

        public sealed class CategorySummaryDto
        {
            public int CategoryId { get; set; }
            public string Name { get; set; } = "";
            public string TypeDisplay { get; set; } = "";
            public int EntryCount { get; set; }
            public decimal TotalAmount { get; set; }
            public double SharePercent { get; set; }
        }

        public sealed class CategoryTransactionDto
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Description { get; set; } = "";
            public int? CategoryId { get; set; }
            public int? AccountId { get; set; }
            public string? CategoryName { get; set; }
            public string? AccountName { get; set; }
            public string? Source { get; set; }
        }

        public static DataTable GetCategories(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            if (ColumnExists(c, "Categories", "IsArchived"))
                cmd.CommandText = "SELECT Id, Name FROM Categories WHERE UserId=@u AND IsArchived = 0 ORDER BY Name;";
            else
                cmd.CommandText = "SELECT Id, Name FROM Categories WHERE UserId=@u ORDER BY Name;";

            cmd.Parameters.AddWithValue("@u", userId);

            var dt = new DataTable();
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }

        public static List<string> GetCategoriesByUser(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            if (ColumnExists(c, "Categories", "IsArchived"))
                cmd.CommandText = "SELECT Name FROM Categories WHERE UserId=@u AND IsArchived = 0 ORDER BY Name;";
            else
                cmd.CommandText = "SELECT Name FROM Categories WHERE UserId=@u ORDER BY Name;";

            cmd.Parameters.AddWithValue("@u", userId);

            var list = new List<string>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));
            return list;
        }

        public static string? GetCategoryName(int categoryId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Categories WHERE Id=@id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", categoryId);
            var obj = cmd.ExecuteScalar();
            return obj == null || obj == DBNull.Value ? null : obj.ToString();
        }

        public static int? GetCategoryIdByName(int userId, string name)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            var sql = @"
SELECT Id 
FROM Categories 
WHERE UserId=@u AND lower(Name)=lower(@n)";

            if (ColumnExists(c, "Categories", "IsArchived"))
                sql += " AND IsArchived = 0";

            sql += " LIMIT 1;";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name.Trim());
            var obj = cmd.ExecuteScalar();
            return (obj == null || obj == DBNull.Value) ? (int?)null : Convert.ToInt32(obj);
        }

        public static int CreateCategory(int userId, string name)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO Categories(UserId, Name) VALUES(@u,@n);
                                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name.Trim());
            var id = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            RaiseDataChanged();
            return id;
        }

        public static int GetOrCreateCategoryId(int userId, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nazwa kategorii pusta.", nameof(name));
            var existing = GetCategoryIdByName(userId, name);
            return existing ?? CreateCategory(userId, name);
        }

        public static int GetOrCreateCategoryId(string name, int userId) => GetOrCreateCategoryId(userId, name);

        public static void UpdateCategory(int id, string name)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE Categories SET Name=@n WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@n", name.Trim());
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void DeleteCategory(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Categories WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void ArchiveCategory(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            if (!ColumnExists(c, "Categories", "IsArchived"))
            {
                cmd.CommandText = "DELETE FROM Categories WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                RaiseDataChanged();
                return;
            }

            cmd.CommandText = "UPDATE Categories SET IsArchived = 1 WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void MergeCategories(int userId, int sourceCategoryId, int targetCategoryId)
        {
            if (sourceCategoryId == targetCategoryId) return;

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

            using (var check = c.CreateCommand())
            {
                check.Transaction = tx;
                check.CommandText = @"
SELECT COUNT(*) 
FROM Categories 
WHERE UserId=@u AND Id IN (@src, @tgt);";
                check.Parameters.AddWithValue("@u", userId);
                check.Parameters.AddWithValue("@src", sourceCategoryId);
                check.Parameters.AddWithValue("@tgt", targetCategoryId);
                var count = Convert.ToInt32(check.ExecuteScalar() ?? 0);
                if (count < 2)
                    throw new InvalidOperationException("Nie znaleziono kategorii lub nie nale¿¹ do u¿ytkownika.");
            }

            using (var upd = c.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = @"
UPDATE Expenses
SET CategoryId = @tgt
WHERE UserId = @u AND CategoryId = @src;";
                upd.Parameters.AddWithValue("@tgt", targetCategoryId);
                upd.Parameters.AddWithValue("@u", userId);
                upd.Parameters.AddWithValue("@src", sourceCategoryId);
                upd.ExecuteNonQuery();
            }

            using (var arch = c.CreateCommand())
            {
                arch.Transaction = tx;

                if (ColumnExists(c, "Categories", "IsArchived"))
                    arch.CommandText = @"UPDATE Categories SET IsArchived = 1 WHERE Id=@src AND UserId=@u;";
                else
                    arch.CommandText = @"DELETE FROM Categories WHERE Id=@src AND UserId=@u;";

                arch.Parameters.AddWithValue("@src", sourceCategoryId);
                arch.Parameters.AddWithValue("@u", userId);
                arch.ExecuteNonQuery();
            }

            tx.Commit();
            RaiseDataChanged();
        }

        public static List<CategorySummaryDto> GetCategorySummary(
            int userId,
            DateTime from,
            DateTime to,
            int? typeFilter = null,
            string? search = null)
        {
            var result = new List<CategorySummaryDto>();

            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();

            var sb = new StringBuilder(@"
SELECT 
    c.Id,
    c.Name,
    c.Type,
    COUNT(e.Id) AS EntryCount,
    IFNULL(SUM(e.Amount), 0) AS TotalAmount
FROM Categories c
LEFT JOIN Expenses e
    ON e.CategoryId = c.Id
    AND e.UserId = @uid
    AND date(e.Date) BETWEEN date(@from) AND date(@to)
WHERE c.UserId = @uid
");

            if (ColumnExists(con, "Categories", "IsArchived"))
                sb.Append(" AND c.IsArchived = 0");

            if (typeFilter.HasValue && ColumnExists(con, "Categories", "Type"))
            {
                sb.Append(" AND c.Type = @type");
                cmd.Parameters.AddWithValue("@type", typeFilter.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                sb.Append(" AND lower(c.Name) LIKE @search");
                cmd.Parameters.AddWithValue("@search", "%" + search.Trim().ToLowerInvariant() + "%");
            }

            sb.Append(@"
GROUP BY c.Id, c.Name, c.Type
ORDER BY TotalAmount DESC;");

            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using var r = cmd.ExecuteReader();

            var buffer = new List<(int id, string name, int type, int entries, decimal total)>();
            decimal totalAll = 0m;

            while (r.Read())
            {
                var id = r.GetInt32(0);
                var name = GetStringSafe(r, 1);
                var type = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                var entries = r.IsDBNull(3) ? 0 : r.GetInt32(3);
                var total = r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetValue(4));

                buffer.Add((id, name, type, entries, total));
                totalAll += total;
            }

            foreach (var x in buffer)
            {
                double share = totalAll > 0 ? (double)(x.total / totalAll * 100m) : 0;

                result.Add(new CategorySummaryDto
                {
                    CategoryId = x.id,
                    Name = x.name,
                    TypeDisplay = x.type switch
                    {
                        1 => "Przychód",
                        2 => "Obie",
                        _ => "Wydatek"
                    },
                    EntryCount = x.entries,
                    TotalAmount = x.total,
                    SharePercent = Math.Round(share, 1)
                });
            }

            return result;
        }

        public static List<CategoryTransactionDto> GetLastTransactionsForCategory(int userId, int categoryId, int limit = 10)
        {
            var list = new List<CategoryTransactionDto>();

            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Date, Amount, Description
FROM Expenses
WHERE UserId = @uid AND CategoryId = @cid
ORDER BY date(Date) DESC, Id DESC
LIMIT @limit;";

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@cid", categoryId);
            cmd.Parameters.AddWithValue("@limit", limit);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var date = GetDate(r, 0);
                var val = r.GetValue(1);
                decimal amount = Convert.ToDecimal(val);
                var desc = GetNullableString(r, 2) ?? string.Empty;

                list.Add(new CategoryTransactionDto
                {
                    Date = date,
                    Amount = amount,
                    Description = desc
                });
            }

            return list;
        }

        public static List<(int Id, string Name, string? Color, string? Icon)> GetCategoriesExtended(int userId)
        {
            var list = new List<(int, string, string?, string?)>();
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            if (ColumnExists(c, "Categories", "IsArchived"))
                cmd.CommandText = "SELECT Id, Name, Color, Icon FROM Categories WHERE UserId=@u AND IsArchived=0 ORDER BY Name;";
            else
                cmd.CommandText = "SELECT Id, Name, Color, Icon FROM Categories WHERE UserId=@u ORDER BY Name;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                var name = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                var color = r.IsDBNull(2) ? null : r.GetString(2);
                var icon = r.IsDBNull(3) ? null : r.GetString(3);
                list.Add((id, name, color, icon));
            }
            return list;
        }

        public static void UpdateCategoryFull(int id, int userId, string? name = null, string? color = null, string? icon = null)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            var setParts = new List<string>();
            if (name != null) setParts.Add("Name=@n");
            if (color != null) setParts.Add("Color=@c");
            if (icon != null) setParts.Add("Icon=@i");
            if (setParts.Count == 0) return;

            cmd.CommandText = $"UPDATE Categories SET {string.Join(", ", setParts)} WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", userId);
            if (name != null) cmd.Parameters.AddWithValue("@n", name.Trim());
            if (color != null) cmd.Parameters.AddWithValue("@c", (object?)color ?? DBNull.Value);
            if (icon != null) cmd.Parameters.AddWithValue("@i", (object?)icon ?? DBNull.Value);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        // =========================================================
        // ========================= ACCOUNTS =======================
        // =========================================================

        public static DataTable GetAccountsTable(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT 
    Id,
    BankName,
    AccountName,
    Iban,
    Currency,
    Balance
FROM BankAccounts
WHERE UserId=@u
ORDER BY AccountName;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            var dt = new DataTable();
            dt.Load(r);
            return dt;
        }

        public static List<BankAccountModel> GetAccounts(int userId)
        {
            var list = new List<BankAccountModel>();
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT 
    Id,
    UserId,
    ConnectionId,
    BankName,
    AccountName,
    Iban,
    Currency,
    Balance
FROM BankAccounts 
WHERE UserId=@u 
ORDER BY AccountName;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BankAccountModel
                {
                    Id = r.GetInt32(0),
                    UserId = r.GetInt32(1),
                    ConnectionId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    BankName = GetStringSafe(r, 3),
                    AccountName = GetStringSafe(r, 4),
                    Iban = GetStringSafe(r, 5),
                    Currency = string.IsNullOrWhiteSpace(GetStringSafe(r, 6)) ? "PLN" : GetStringSafe(r, 6),
                    Balance = r.IsDBNull(7) ? 0m : Convert.ToDecimal(r.GetValue(7))
                });
            }
            return list;
        }

        public static int InsertAccount(BankAccountModel a)
        {
            using var c = OpenAndEnsureSchema();

            if (a.ConnectionId is int cid)
            {
                using var chk = c.CreateCommand();
                chk.CommandText = "SELECT 1 FROM BankConnections WHERE Id=@cid LIMIT 1;";
                chk.Parameters.AddWithValue("@cid", cid);
                if (chk.ExecuteScalar() is null) a.ConnectionId = null;
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO BankAccounts(UserId, ConnectionId, BankName, AccountName, Iban, Currency, Balance)
VALUES (@u, @conn, @bank, @name, @iban, @cur, @bal);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", a.UserId);
            cmd.Parameters.AddWithValue("@conn", (object?)a.ConnectionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bank", a.BankName ?? "");
            cmd.Parameters.AddWithValue("@name", a.AccountName ?? "");
            cmd.Parameters.AddWithValue("@iban", a.Iban ?? "");
            cmd.Parameters.AddWithValue("@cur", string.IsNullOrWhiteSpace(a.Currency) ? "PLN" : a.Currency);
            cmd.Parameters.AddWithValue("@bal", a.Balance);

            var id = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            RaiseDataChanged();
            return id;
        }

        public static void UpdateAccount(BankAccountModel a)
        {
            using var c = OpenAndEnsureSchema();

            EnsureValidConnectionId(c, a.Id);

            if (a.ConnectionId is int cid)
            {
                using var chk = c.CreateCommand();
                chk.CommandText = "SELECT 1 FROM BankConnections WHERE Id=@cid LIMIT 1;";
                chk.Parameters.AddWithValue("@cid", cid);
                if (chk.ExecuteScalar() is null) a.ConnectionId = null;
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE BankAccounts SET
    ConnectionId=@conn, 
    BankName=@bank,
    AccountName=@name, 
    Iban=@iban, 
    Currency=@cur, 
    Balance=@bal
WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.Parameters.AddWithValue("@u", a.UserId);
            cmd.Parameters.AddWithValue("@conn", (object?)a.ConnectionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bank", a.BankName ?? "");
            cmd.Parameters.AddWithValue("@name", a.AccountName ?? "");
            cmd.Parameters.AddWithValue("@iban", a.Iban ?? "");
            cmd.Parameters.AddWithValue("@cur", string.IsNullOrWhiteSpace(a.Currency) ? "PLN" : a.Currency);
            cmd.Parameters.AddWithValue("@bal", a.Balance);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void DeleteAccount(int id, int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM BankAccounts WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        /// <summary>
        /// Jeœli BankAccounts.ConnectionId wskazuje na nieistniej¹cy rekord – ustawia NULL.
        /// </summary>
        private static void EnsureValidConnectionId(SqliteConnection c, int accountId)
        {
            using var get = c.CreateCommand();
            get.CommandText = "SELECT ConnectionId FROM BankAccounts WHERE Id=@id LIMIT 1;";
            get.Parameters.AddWithValue("@id", accountId);
            var raw = get.ExecuteScalar();

            if (raw is null || raw == DBNull.Value) return;

            var cid = Convert.ToInt32(raw);

            using var chk = c.CreateCommand();
            chk.CommandText = "SELECT 1 FROM BankConnections WHERE Id=@cid LIMIT 1;";
            chk.Parameters.AddWithValue("@cid", cid);
            var exists = chk.ExecuteScalar() != null;
            if (exists) return;

            using var fix = c.CreateCommand();
            fix.CommandText = "UPDATE BankAccounts SET ConnectionId=NULL WHERE Id=@id;";
            fix.Parameters.AddWithValue("@id", accountId);
            fix.ExecuteNonQuery();
        }

        // =========================================================
        // ========================= ENVELOPES ======================
        // =========================================================

        public sealed class EnvelopeGoalDto
        {
            public int EnvelopeId { get; set; }
            public string Name { get; set; } = "";
            public decimal Target { get; set; }
            public decimal Allocated { get; set; }
            public DateTime? Deadline { get; set; }
            public string? GoalText { get; set; }
        }

        public static DataTable GetEnvelopesTable(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT 
    Id,
    UserId,
    Name,
    Target,
    Allocated,
    Note,
    CreatedAt
FROM Envelopes
WHERE UserId=@u
ORDER BY Name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("@u", userId);

            var dt = new DataTable();
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }

        public static List<string> GetEnvelopeNames(int userId) => GetEnvelopesNames(userId);

        public static List<string> GetEnvelopesNames(int userId)
        {
            var result = new List<string>();

            try
            {
                using var conn = OpenAndEnsureSchema();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT Name
FROM Envelopes
WHERE UserId = @uid
ORDER BY Name;";
                cmd.Parameters.AddWithValue("@uid", userId);

                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var name = rd["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        result.Add(name);
                }
            }
            catch { }

            return result;
        }

        public static int? GetEnvelopeIdByName(int userId, string name)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT Id
FROM Envelopes
WHERE UserId=@u AND Name=@n
LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name.Trim());
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) return null;
            return Convert.ToInt32(obj);
        }

        public static List<EnvelopeGoalDto> GetEnvelopeGoals(int userId)
        {
            var list = new List<EnvelopeGoalDto>();

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            bool hasDeadline = ColumnExists(c, "Envelopes", "Deadline");
            bool hasGoalText = ColumnExists(c, "Envelopes", "GoalText");

            var sql = @"
SELECT 
    Id,
    Name,
    Target,
    COALESCE(Allocated,0) AS Allocated";

            sql += hasDeadline ? ", Deadline" : ", NULL AS Deadline";
            sql += hasGoalText ? ", GoalText" : ", Note AS GoalText";

            sql += @"
FROM Envelopes
WHERE UserId = @u
  AND Target IS NOT NULL
  AND Target > 0";

            if (hasDeadline) sql += " AND Deadline IS NOT NULL";

            sql += @"
ORDER BY Name COLLATE NOCASE;";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dto = new EnvelopeGoalDto
                {
                    EnvelopeId = r.GetInt32(0),
                    Name = GetStringSafe(r, 1),
                    Target = r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2)),
                    Allocated = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
                    GoalText = GetNullableString(r, 5)
                };

                if (hasDeadline)
                {
                    var dt = GetDate(r, 4);
                    dto.Deadline = dt == DateTime.MinValue ? (DateTime?)null : dt;
                }

                if (dto.Deadline == null && !string.IsNullOrWhiteSpace(dto.GoalText))
                {
                    dto.Deadline = TryParseDeadlineFromGoalText(dto.GoalText);
                }

                list.Add(dto);
            }

            return list;
        }

        private static DateTime? TryParseDeadlineFromGoalText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line.StartsWith("Termin:", StringComparison.OrdinalIgnoreCase))
                {
                    var dateText = line.Substring(7).Trim();

                    if (DateTime.TryParseExact(
                            dateText,
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var d1))
                    {
                        return d1.Date;
                    }

                    if (DateTime.TryParse(
                            dateText,
                            CultureInfo.CurrentCulture,
                            DateTimeStyles.None,
                            out var d2))
                    {
                        return d2.Date;
                    }
                }
            }

            return null;
        }

        public static void ClearEnvelopeGoal(int userId, int envelopeId)
        {
            using var c = OpenAndEnsureSchema();

            bool hasDeadline = ColumnExists(c, "Envelopes", "Deadline");
            bool hasGoalText = ColumnExists(c, "Envelopes", "GoalText");

            var sb = new StringBuilder("UPDATE Envelopes SET ");
            bool first = true;

            if (hasDeadline)
            {
                sb.Append("Deadline = NULL");
                first = false;
            }

            if (hasGoalText)
            {
                if (!first) sb.Append(", ");
                sb.Append("GoalText = NULL");
            }
            else
            {
                if (!first) sb.Append(", ");
                sb.Append("Note = NULL");
            }

            sb.Append(" WHERE Id=@id AND UserId=@u;");

            using var cmd = c.CreateCommand();
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("@id", envelopeId);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void UpdateEnvelopeGoal(
            int userId,
            int envelopeId,
            decimal target,
            decimal allocated,
            DateTime deadline,
            string? goalText)
        {
            using var c = OpenAndEnsureSchema();

            bool hasDeadline = ColumnExists(c, "Envelopes", "Deadline");
            bool hasGoalText = ColumnExists(c, "Envelopes", "GoalText");

            var sb = new StringBuilder(@"
UPDATE Envelopes
   SET Target   = @t,
       Allocated = @a");

            if (hasDeadline)
                sb.Append(", Deadline = @d");

            if (hasGoalText)
                sb.Append(", GoalText = @g");
            else
                sb.Append(", Note = @g");

            sb.Append(" WHERE Id=@id AND UserId=@u;");

            using var cmd = c.CreateCommand();
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("@t", target);
            cmd.Parameters.AddWithValue("@a", allocated);

            if (hasDeadline)
                cmd.Parameters.AddWithValue("@d", deadline.ToString("yyyy-MM-dd"));

            cmd.Parameters.AddWithValue("@g", (object?)goalText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", envelopeId);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static int InsertEnvelope(int userId, string name, decimal target, decimal allocated, string? note)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Envelopes(UserId, Name, Target, Allocated, Note, CreatedAt)
VALUES (@u, @n, @t, @a, @note, CURRENT_TIMESTAMP);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@t", target);
            cmd.Parameters.AddWithValue("@a", allocated);
            cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);

            var id = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            RaiseDataChanged();
            return id;
        }

        public static void UpdateEnvelope(int id, int userId, string name, decimal target, decimal allocated, string? note)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE Envelopes
SET Name=@n, Target=@t, Allocated=@a, Note=@note
WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@t", target);
            cmd.Parameters.AddWithValue("@a", allocated);
            cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void DeleteEnvelope(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Envelopes WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        // =========================================================
        // ======================= CASH / SAVED (READ) ==============
        // =========================================================
        // Uwaga: zapisy/transfery robi LedgerService. Tu zostawiamy tylko bezpieczne odczyty,
        // bo UI czêsto chce snapshoty.

        public static decimal GetCashOnHand(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(Amount,0) FROM CashOnHand WHERE UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        public static decimal GetSavedCash(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(Amount,0) FROM SavedCash WHERE UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        public static decimal GetTotalAllocatedInEnvelopesForUser(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Allocated),0) FROM Envelopes WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        private static decimal GetTotalBanksBalance(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Balance),0) FROM BankAccounts WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        public sealed class MoneySnapshot
        {
            public decimal Banks { get; set; }
            public decimal Cash { get; set; }       // wolna
            public decimal Saved { get; set; }      // ca³a pula od³o¿onej
            public decimal Envelopes { get; set; }  // przydzielona do kopert

            public decimal SavedUnallocated => Saved - Envelopes;
            public decimal Total => Banks + Cash + Saved;
        }

        public static MoneySnapshot GetMoneySnapshot(int userId)
        {
            var banks = GetTotalBanksBalance(userId);

            var allCash = GetCashOnHand(userId);
            var savedCash = GetSavedCash(userId);

            var freeCash = Math.Max(0m, allCash - savedCash);
            var allocated = GetTotalAllocatedInEnvelopesForUser(userId);

            return new MoneySnapshot
            {
                Banks = banks,
                Cash = freeCash,
                Saved = savedCash,
                Envelopes = allocated
            };
        }

        // =========================================================
        // ========================= EXPENSES =======================
        // =========================================================

        public static DataTable GetExpenses(
            int userId,
            DateTime? from = null,
            DateTime? to = null,
            int? categoryId = null,
            string? search = null,
            int? accountId = null)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();

            bool hasAccountId = ColumnExists(con, "Expenses", "AccountId");

            var sb = new StringBuilder(@"
SELECT 
    e.Id,
    e.UserId,
    e.Date,
    e.Amount,
    e.Description,
    e.CategoryId,
    COALESCE(c.Name,'(brak)') AS CategoryName,
    " + (hasAccountId ? "e.AccountId" : "NULL") + @" AS AccountId,
    e.IsPlanned
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId = @uid");

            cmd.Parameters.AddWithValue("@uid", userId);

            if (from != null)
            {
                sb.Append(" AND date(e.Date) >= date(@from)");
                cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
            }

            if (to != null)
            {
                sb.Append(" AND date(e.Date) <= date(@to)");
                cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));
            }

            if (categoryId != null)
            {
                sb.Append(" AND e.CategoryId = @cid");
                cmd.Parameters.AddWithValue("@cid", categoryId.Value);
            }

            if (accountId != null && hasAccountId)
            {
                sb.Append(" AND e.AccountId = @acc");
                cmd.Parameters.AddWithValue("@acc", accountId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                sb.Append(" AND lower(e.Description) LIKE @q");
                cmd.Parameters.AddWithValue("@q", "%" + search.Trim().ToLower() + "%");
            }

            sb.Append(" ORDER BY date(e.Date) DESC, e.Id DESC;");
            cmd.CommandText = sb.ToString();

            var dt = new DataTable();
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }

        public static List<ExpenseDisplayModel> GetExpensesWithCategory()
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT e.Id, e.UserId, e.Amount, e.Date, e.Description,
       COALESCE(c.Name,'') AS CategoryName
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
ORDER BY e.Date DESC, e.Id DESC;";

            var list = new List<ExpenseDisplayModel>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ExpenseDisplayModel
                {
                    Id = r.GetInt32(0),
                    UserId = r.GetInt32(1),
                    Amount = r.GetDouble(2),
                    Date = GetDate(r, 3),
                    Description = GetNullableString(r, 4) ?? string.Empty,
                    CategoryName = GetStringSafe(r, 5)
                });
            }
            return list;
        }

        public static List<ExpenseDisplayModel> GetExpensesWithCategoryNameByUser(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT e.Id, e.UserId, e.Amount, e.Date, e.Description,
       COALESCE(c.Name,'') AS CategoryName
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId=@u
ORDER BY e.Date DESC, e.Id DESC;";
            cmd.Parameters.AddWithValue("@u", userId);

            var list = new List<ExpenseDisplayModel>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ExpenseDisplayModel
                {
                    Id = r.GetInt32(0),
                    UserId = r.GetInt32(1),
                    Amount = r.GetDouble(2),
                    Date = GetDate(r, 3),
                    Description = GetNullableString(r, 4) ?? string.Empty,
                    CategoryName = GetStringSafe(r, 5)
                });
            }
            return list;
        }

        public static Expense? GetExpenseById(int id)
        {
            using var c = OpenAndEnsureSchema();

            bool hasAccountId = ColumnExists(c, "Expenses", "AccountId");
            bool hasAccountText = ColumnExists(c, "Expenses", "Account"); // legacy

            using var cmd = c.CreateCommand();

            var selectAccountPart =
                hasAccountId ? "AccountId" :
                hasAccountText ? "Account" :
                "NULL";

            cmd.CommandText = $@"
SELECT Id, UserId, Amount, Date, Description, CategoryId, {selectAccountPart} AS AccountOrId, IsPlanned
FROM Expenses
WHERE Id=@id
LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var catId = r.IsDBNull(5) ? 0 : r.GetInt32(5);

            string accountText = "";
            if (!r.IsDBNull(6))
            {
                var raw = r.GetValue(6);
                accountText = raw?.ToString() ?? "";
            }

            return new Expense
            {
                Id = r.GetInt32(0),
                UserId = r.GetInt32(1),
                Amount = Convert.ToDouble(r.GetValue(2)),
                Date = GetDate(r, 3),
                Description = GetNullableString(r, 4),
                CategoryId = catId,
                Account = accountText,
                IsPlanned = !r.IsDBNull(7) && Convert.ToInt32(r.GetValue(7)) == 1
            };
        }

        private static int? TryResolveAccountIdFromExpenseAccountText(SqliteConnection c, int userId, string? accountText)
        {
            if (string.IsNullOrWhiteSpace(accountText)) return null;

            var t = accountText.Trim();

            if (t.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
            {
                var name = t.Substring("Konto:".Length).Trim();
                if (string.IsNullOrWhiteSpace(name)) return null;

                using var cmd = c.CreateCommand();
                cmd.CommandText = @"SELECT Id FROM BankAccounts WHERE UserId=@u AND AccountName=@n LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@n", name);
                var obj = cmd.ExecuteScalar();
                return (obj == null || obj == DBNull.Value) ? (int?)null : Convert.ToInt32(obj);
            }

            return null;
        }

        public static int InsertExpense(Expense e)
        {
            using var c = OpenAndEnsureSchema();

            bool hasAccountId = ExpensesHasAccountId(c);
            bool hasAccountText = ColumnExists(c, "Expenses", "Account"); // legacy
            bool hasBudgetId = ExpensesHasBudgetId(c);

            bool hasPaymentKind = ColumnExists(c, "Expenses", "PaymentKind");
            bool hasPaymentRefId = ColumnExists(c, "Expenses", "PaymentRefId");

            using var cmd = c.CreateCommand();

            var cols = new List<string> { "UserId", "Amount", "Date", "Description", "CategoryId", "IsPlanned" };
            var vals = new List<string> { "@u", "@a", "@d", "@desc", "@c", "@planned" };

            if (hasAccountId) { cols.Add("AccountId"); vals.Add("@accId"); }
            if (hasAccountText) { cols.Add("Account"); vals.Add("@accText"); }
            if (hasBudgetId) { cols.Add("BudgetId"); vals.Add("@budgetId"); }

            // NOWE: stabilne pola
            if (hasPaymentKind) { cols.Add("PaymentKind"); vals.Add("@pk"); }
            if (hasPaymentRefId) { cols.Add("PaymentRefId"); vals.Add("@pr"); }

            cmd.CommandText = $@"
INSERT INTO Expenses({string.Join(",", cols)})
VALUES ({string.Join(",", vals)});
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@u", e.UserId);
            cmd.Parameters.AddWithValue("@a", e.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);

            if (e.CategoryId > 0) cmd.Parameters.AddWithValue("@c", e.CategoryId);
            else cmd.Parameters.AddWithValue("@c", DBNull.Value);

            cmd.Parameters.AddWithValue("@planned", e.IsPlanned ? 1 : 0);

            // AccountId (legacy) – ustawiamy NULL, bo ju¿ nie parsujemy tekstu do ksiêgowania.
            // Dla kompatybilnoœci mo¿esz zostawiæ próbê ustawienia AccountId dla banku:
            int? legacyAccountId = null;
            if (hasAccountId && e.PaymentKind == PaymentKind.BankAccount && e.PaymentRefId.HasValue)
                legacyAccountId = e.PaymentRefId.Value;

            if (hasAccountId) cmd.Parameters.AddWithValue("@accId", (object?)legacyAccountId ?? DBNull.Value);
            if (hasAccountText) cmd.Parameters.AddWithValue("@accText", (object?)e.Account ?? DBNull.Value);
            if (hasBudgetId) cmd.Parameters.AddWithValue("@budgetId", (object?)e.BudgetId ?? DBNull.Value);

            if (hasPaymentKind) cmd.Parameters.AddWithValue("@pk", (int)e.PaymentKind);
            if (hasPaymentRefId) cmd.Parameters.AddWithValue("@pr", (object?)e.PaymentRefId ?? DBNull.Value);

            var rowId = (long)(cmd.ExecuteScalar() ?? 0L);

            // Ksiêgowanie TYLKO raz i TYLKO dla nieplanowanych
            if (!e.IsPlanned)
            {
                var amt = Convert.ToDecimal(e.Amount);

                switch (e.PaymentKind)
                {
                    case PaymentKind.FreeCash:
                        TransactionsFacadeService.SpendFromFreeCash(e.UserId, amt);
                        break;

                    case PaymentKind.SavedCash:
                        TransactionsFacadeService.SpendFromSavedCash(e.UserId, amt);
                        break;

                    case PaymentKind.BankAccount:
                        if (!e.PaymentRefId.HasValue)
                            throw new InvalidOperationException("Brak PaymentRefId dla p³atnoœci z konta bankowego.");
                        TransactionsFacadeService.SpendFromBankAccount(e.UserId, e.PaymentRefId.Value, amt);
                        break;

                    case PaymentKind.Envelope:
                        if (!e.PaymentRefId.HasValue)
                            throw new InvalidOperationException("Brak PaymentRefId dla p³atnoœci z koperty.");
                        TransactionsFacadeService.SpendFromEnvelope(e.UserId, e.PaymentRefId.Value, amt);
                        break;

                    default:
                        throw new InvalidOperationException("Nieznany PaymentKind.");
                }
            }

            RaiseDataChanged();
            return (int)rowId;
        }


        public static void UpdateExpense(Expense e)
        {
            using var c = OpenAndEnsureSchema();

            bool hasAccountId = ExpensesHasAccountId(c);
            bool hasAccountText = ColumnExists(c, "Expenses", "Account"); // legacy
            bool hasBudgetId = ExpensesHasBudgetId(c);

            var old = GetExpenseById(e.Id);

            // 1) Cofnij wp³yw starego wydatku (jeœli by³ zrealizowany) – robimy to przez Ledger delete + reinsert?
            // Stabilniejszy wariant: u¿yj Ledger usuwania + update rekordu? Nie – bo update ma zachowaæ Id.
            // Tu robimy minimalnie: odwracamy saldo "rêcznie" tylko przez Ledger: usuwamy transakcjê i potem wstawiamy ponownie? odpada.
            // Dlatego: cofniêcie realizujemy przez LedgerService.DeleteTransactionAndRevertBalance na KOPII rekordu?
            // To usunê³oby wiersz. Nie.
            //
            // W praktyce: cofniêcie starego wp³ywu robimy lokalnie, ale TYLKO jako operacja ksiêgowa – bez duplikowania logiki transferów.
            // U¿ywamy LedgerService jako prymitywów: dodajemy œrodki "w drug¹ stronê" nie mamy publicznych API.
            // Dlatego w ETAPIE 1 zostawiamy mechanikê: jeœli old by³ zrealizowany, to najproœciej:
            // - usuñ rekord i wstaw nowy (utrata Id) – odpada.
            //
            // Wniosek: na teraz utrzymujemy dotychczasowy bezpieczny mechanizm update:
            // - odwrócenie starego wp³ywu robimy tu bez transferów, tylko jako "add back" (bank/cash),
            // - na³o¿enie nowego wp³ywu robimy przez TransactionsFacadeService.Spend*.
            //
            // To NIE dubluje logiki (bo Transfery i DeleteTransaction s¹ w Ledger),
            // a update expense to specyficzna operacja.

            if (old != null && !old.IsPlanned)
            {
                int? oldAccId = null;

                if (hasAccountId)
                {
                    using var cmdOldAcc = c.CreateCommand();
                    cmdOldAcc.CommandText = "SELECT AccountId FROM Expenses WHERE Id=@id LIMIT 1;";
                    cmdOldAcc.Parameters.AddWithValue("@id", e.Id);
                    var raw = cmdOldAcc.ExecuteScalar();
                    if (raw != null && raw != DBNull.Value)
                        oldAccId = Convert.ToInt32(raw);
                }

                if (!oldAccId.HasValue && hasAccountText)
                {
                    using var cmdOldTxt = c.CreateCommand();
                    cmdOldTxt.CommandText = "SELECT Account FROM Expenses WHERE Id=@id LIMIT 1;";
                    cmdOldTxt.Parameters.AddWithValue("@id", e.Id);
                    var txt = cmdOldTxt.ExecuteScalar()?.ToString();

                    var resolved = TryResolveAccountIdFromExpenseAccountText(c, old.UserId, txt);
                    if (resolved.HasValue)
                        oldAccId = resolved.Value;
                }

                if (oldAccId.HasValue)
                {
                    using var addBack = c.CreateCommand();
                    addBack.CommandText = @"UPDATE BankAccounts
SET Balance = Balance + @a
WHERE Id=@id AND UserId=@u;";
                    addBack.Parameters.AddWithValue("@a", Convert.ToDecimal(old.Amount));
                    addBack.Parameters.AddWithValue("@id", oldAccId.Value);
                    addBack.Parameters.AddWithValue("@u", old.UserId);
                    addBack.ExecuteNonQuery();
                }
                else
                {
                    using var addCash = c.CreateCommand();
                    addCash.CommandText = @"
INSERT INTO CashOnHand(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE
SET Amount = CashOnHand.Amount + excluded.Amount,
    UpdatedAt = CURRENT_TIMESTAMP;";
                    addCash.Parameters.AddWithValue("@u", old.UserId);
                    addCash.Parameters.AddWithValue("@a", Convert.ToDecimal(old.Amount));
                    addCash.ExecuteNonQuery();
                }
            }

            int? newAccountId = null;
            if (hasAccountId)
                newAccountId = TryResolveAccountIdFromExpenseAccountText(c, e.UserId, e.Account);

            var setParts = new List<string>
            {
                "UserId=@u",
                "Amount=@a",
                "Date=@d",
                "Description=@desc",
                "CategoryId=@c",
                "IsPlanned=@planned"
            };

            if (hasAccountId) setParts.Add("AccountId=@accId");
            if (hasAccountText) setParts.Add("Account=@accText");
            if (hasBudgetId) setParts.Add("BudgetId=@budgetId");

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = $@"
UPDATE Expenses SET
 {string.Join(",\n ", setParts)}
WHERE Id=@id;";

                cmd.Parameters.AddWithValue("@id", e.Id);
                cmd.Parameters.AddWithValue("@u", e.UserId);
                cmd.Parameters.AddWithValue("@a", e.Amount);
                cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
                cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);

                if (e.CategoryId is int cid && cid > 0) cmd.Parameters.AddWithValue("@c", cid);
                else cmd.Parameters.AddWithValue("@c", DBNull.Value);

                cmd.Parameters.AddWithValue("@planned", e.IsPlanned ? 1 : 0);

                if (hasAccountId) cmd.Parameters.AddWithValue("@accId", (object?)newAccountId ?? DBNull.Value);
                if (hasAccountText) cmd.Parameters.AddWithValue("@accText", (object?)e.Account ?? DBNull.Value);
                if (hasBudgetId) cmd.Parameters.AddWithValue("@budgetId", (object?)e.BudgetId ?? DBNull.Value);

                cmd.ExecuteNonQuery();
            }

            if (!e.IsPlanned)
            {
                if (hasAccountId && newAccountId.HasValue)
                    TransactionsFacadeService.SpendFromBankAccount(e.UserId, newAccountId.Value, Convert.ToDecimal(e.Amount));
                else
                    TransactionsFacadeService.SpendFromFreeCash(e.UserId, Convert.ToDecimal(e.Amount));
            }

            RaiseDataChanged();
        }

        public static void AddExpense(Expense e) => _ = InsertExpense(e);

        public static void DeleteExpense(int id)
        {
            if (id <= 0) return;
            TransactionsFacadeService.DeleteTransaction(id);
        }

        // =========================================================
        // ========================= TRANSFERS ======================
        // =========================================================

        public static DataTable GetTransfers(int userId, DateTime? from = null, DateTime? to = null)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();

            var sb = new StringBuilder(@"
SELECT Id, UserId, Date, Amount, Description, FromKind, FromRefId, ToKind, ToRefId, IsPlanned
FROM Transfers
WHERE UserId=@u");

            cmd.Parameters.AddWithValue("@u", userId);

            if (from.HasValue)
            {
                sb.Append(" AND date(Date) >= date(@from)");
                cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
            }

            if (to.HasValue)
            {
                sb.Append(" AND date(Date) <= date(@to)");
                cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));
            }

            sb.Append(" ORDER BY date(Date) DESC, Id DESC;");
            cmd.CommandText = sb.ToString();

            var dt = new DataTable();
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }

        public static void InsertTransfer(
            int userId,
            DateTime date,
            decimal amount,
            string fromKind,
            int? fromRefId,
            string toKind,
            int? toRefId,
            string? description = null,
            bool isPlanned = false)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Transfers(UserId, Date, Amount, Description, FromKind, FromRefId, ToKind, ToRefId, IsPlanned)
VALUES (@u,@d,@a,@desc,@fk,@fr,@tk,@tr,@p);";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@d", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fk", fromKind);
            cmd.Parameters.AddWithValue("@fr", (object?)fromRefId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tk", toKind);
            cmd.Parameters.AddWithValue("@tr", (object?)toRefId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@p", isPlanned ? 1 : 0);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        // =========================================================
        // ========================= INCOMES ========================
        // =========================================================

        public static DataTable GetIncomes(int userId, DateTime? from = null, DateTime? to = null, bool includePlanned = true)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();

            var sb = new StringBuilder(@"
SELECT i.Id, i.UserId, i.Date, i.Amount, i.Description, i.Source, i.CategoryId, c.Name AS CategoryName, i.IsPlanned
FROM Incomes i
LEFT JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId=@u");

            cmd.Parameters.AddWithValue("@u", userId);

            if (from.HasValue)
            {
                sb.Append(" AND date(i.Date) >= date(@from)");
                cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
            }

            if (to.HasValue)
            {
                sb.Append(" AND date(i.Date) <= date(@to)");
                cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));
            }

            if (!includePlanned) sb.Append(" AND (i.IsPlanned = 0 OR i.IsPlanned IS NULL)");

            sb.Append(" ORDER BY date(i.Date) DESC, i.Id DESC;");
            cmd.CommandText = sb.ToString();

            var dt = new DataTable();
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }

        public static int InsertIncome(
            int userId,
            decimal amount,
            DateTime date,
            string? description,
            string? source,
            int? categoryId,
            bool isPlanned = false,
            int? budgetId = null)
        {
            using var con = OpenAndEnsureSchema();
            bool hasBudgetId = ColumnExists(con, "Incomes", "BudgetId");

            using var cmd = con.CreateCommand();

            cmd.CommandText = hasBudgetId
                ? @"INSERT INTO Incomes(UserId, Amount, Date, Description, Source, CategoryId, IsPlanned, BudgetId)
                    VALUES (@u,@a,@d,@desc,@s,@c,@p,@b);
                    SELECT last_insert_rowid();"
                : @"INSERT INTO Incomes(UserId, Amount, Date, Description, Source, CategoryId, IsPlanned)
                    VALUES (@u,@a,@d,@desc,@s,@c,@p);
                    SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@d", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@s", (object?)source ?? DBNull.Value);
            if (categoryId.HasValue && categoryId.Value > 0) cmd.Parameters.AddWithValue("@c", categoryId.Value);
            else cmd.Parameters.AddWithValue("@c", DBNull.Value);
            cmd.Parameters.AddWithValue("@p", isPlanned ? 1 : 0);

            if (hasBudgetId)
                cmd.Parameters.AddWithValue("@b", (object?)budgetId ?? DBNull.Value);

            var id = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            RaiseDataChanged();
            return id;
        }

        public static void UpdateIncome(
            int id,
            int userId,
            decimal? amount = null,
            string? description = null,
            bool? isPlanned = null,
            DateTime? date = null,
            int? categoryId = null,
            string? source = null,
            int? budgetId = null)
        {
            using var con = OpenAndEnsureSchema();
            bool hasBudgetId = ColumnExists(con, "Incomes", "BudgetId");

            var setParts = new List<string>
            {
                "Amount = COALESCE(@a, Amount)",
                "Description = COALESCE(@desc, Description)",
                "IsPlanned = COALESCE(@p, IsPlanned)",
                "Date = COALESCE(@d, Date)",
                "CategoryId = @c",
                "Source = COALESCE(@s, Source)"
            };

            if (hasBudgetId) setParts.Add("BudgetId = @b");

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"UPDATE Incomes SET {string.Join(", ", setParts)} WHERE Id=@id AND UserId=@u;";

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", (object?)amount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@p", isPlanned.HasValue ? (isPlanned.Value ? 1 : 0) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@d", date.HasValue ? date.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);

            if (categoryId.HasValue && categoryId.Value > 0) cmd.Parameters.AddWithValue("@c", categoryId.Value);
            else cmd.Parameters.AddWithValue("@c", DBNull.Value);

            cmd.Parameters.AddWithValue("@s", (object?)source ?? DBNull.Value);

            if (hasBudgetId) cmd.Parameters.AddWithValue("@b", (object?)budgetId ?? DBNull.Value);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void DeleteIncome(int id)
        {
            if (id <= 0) return;
            TransactionsFacadeService.DeleteTransaction(id);
        }

        // =========================================================
        // ====================== CHART DTOs ========================
        // =========================================================

        public sealed class CategoryAmountDto
        {
            public string Name { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        public static List<CategoryAmountDto> GetSpendingByCategorySafe(int userId, DateTime from, DateTime to)
        {
            var list = new List<CategoryAmountDto>();
            try
            {
                using var c = OpenAndEnsureSchema();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
SELECT COALESCE(c.Name,'(brak)') AS Name, IFNULL(SUM(e.Amount),0) AS Total
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId=@u AND date(e.Date) BETWEEN date(@f) AND date(@t)
GROUP BY c.Name;";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@f", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@t", to.ToString("yyyy-MM-dd"));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var name = r.IsDBNull(0) ? "(brak)" : r.GetString(0);
                    var amount = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
                    list.Add(new CategoryAmountDto { Name = name, Amount = Math.Abs(amount) });
                }
            }
            catch { }
            return list;
        }

        public static List<CategoryAmountDto> GetIncomeBySourceSafe(int userId, DateTime from, DateTime to)
        {
            var list = new List<CategoryAmountDto>();
            try
            {
                using var c = OpenAndEnsureSchema();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
SELECT COALESCE(i.Source,'Przychody') AS Name, IFNULL(SUM(i.Amount),0) AS Total
FROM Incomes i
WHERE i.UserId=@u AND date(i.Date) BETWEEN date(@f) AND date(@t)
GROUP BY i.Source;";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@f", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@t", to.ToString("yyyy-MM-dd"));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var name = r.IsDBNull(0) ? "Przychody" : r.GetString(0);
                    var amount = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
                    list.Add(new CategoryAmountDto { Name = name, Amount = Math.Abs(amount) });
                }
            }
            catch { }
            return list;
        }
    }
}
