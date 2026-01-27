using Finly.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Finly.Services.Features
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
        // ========================= SCHEMA INIT =========================

        private static void EnsureSchemaInternal(SqliteConnection c)
        {
            // Jedyny w³aœciciel schematu/migracji/indeksów:
            SchemaService.Ensure(c);
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
                EnsureSchemaInternal(c);

                _schemaInitialized = true;
            }
        }

        private static SqliteConnection OpenAndEnsureSchema()
        {
            var c = GetConnection();

            if (_schemaInitialized) return c;

            lock (_schemaLock)
            {
                if (!_schemaInitialized)
                {
                    EnsureSchemaInternal(c);
                    _schemaInitialized = true;
                }
            }

            return c;
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

            bool hasSchedule = ColumnExists(c, "Loans", "SchedulePath");

            using var cmd = c.CreateCommand();
            cmd.CommandText = hasSchedule
                ? @"
SELECT Id, UserId, Name, Principal, InterestRate, StartDate, TermMonths, Note, PaymentDay, SchedulePath
FROM Loans
WHERE UserId=@u
ORDER BY Name;"
                : @"
SELECT Id, UserId, Name, Principal, InterestRate, StartDate, TermMonths, Note, PaymentDay
FROM Loans
WHERE UserId=@u
ORDER BY Name;";

            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var m = new LoanModel
                {
                    Id = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    UserId = r.IsDBNull(1) ? userId : r.GetInt32(1),
                    Name = GetStringSafe(r, 2),
                    Principal = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
                    InterestRate = r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetValue(4)),
                    StartDate = GetDate(r, 5),
                    TermMonths = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    Note = GetNullableString(r, 7),
                    PaymentDay = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                    SchedulePath = null
                };

                if (hasSchedule)
                    m.SchedulePath = GetNullableString(r, 9);

                list.Add(m);
            }

            return list;
        }

        public static void SetLoanSchedulePath(int loanId, int userId, string? schedulePath)
        {
            if (loanId <= 0) throw new ArgumentException("Nieprawid³owy loanId.", nameof(loanId));
            if (userId <= 0) throw new ArgumentException("Nieprawid³owy userId.", nameof(userId));

            using var c = OpenAndEnsureSchema();

            if (!ColumnExists(c, "Loans", "SchedulePath"))
                return; // stara baza bez migracji – po Ensure() powinna ju¿ mieæ, ale zostawiamy bezpiecznik

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE Loans
SET SchedulePath = @p
WHERE Id = @id AND UserId = @u;";
            cmd.Parameters.AddWithValue("@id", loanId);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@p", (object?)schedulePath ?? DBNull.Value);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static string? GetLoanSchedulePath(int loanId, int userId)
        {
            if (loanId <= 0 || userId <= 0) return null;

            using var c = OpenAndEnsureSchema();

            if (!ColumnExists(c, "Loans", "SchedulePath"))
                return null;

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT SchedulePath
FROM Loans
WHERE Id=@id AND UserId=@u
LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", loanId);
            cmd.Parameters.AddWithValue("@u", userId);

            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) return null;

            var s = obj.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        public static int InsertLoan(LoanModel loan)
        {
            using var c = OpenAndEnsureSchema();

            bool hasSchedule = ColumnExists(c, "Loans", "SchedulePath");

            using var cmd = c.CreateCommand();
            cmd.CommandText = hasSchedule
                ? @"
INSERT INTO Loans(UserId, Name, Principal, InterestRate, StartDate, TermMonths, Note, PaymentDay, SchedulePath)
VALUES (@u, @n, @p, @ir, @d, @tm, @note, @pd, @sp);
SELECT last_insert_rowid();"
                : @"
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

            if (hasSchedule)
                cmd.Parameters.AddWithValue("@sp", (object?)loan.SchedulePath ?? DBNull.Value);

            var id = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            RaiseDataChanged();
            return id;
        }


        public static void UpdateLoan(LoanModel loan)
        {
            using var c = OpenAndEnsureSchema();

            bool hasSchedule = ColumnExists(c, "Loans", "SchedulePath");

            using var cmd = c.CreateCommand();
            cmd.CommandText = hasSchedule
                ? @"
UPDATE Loans
   SET Name = @n,
       Principal = @p,
       InterestRate = @ir,
       StartDate = @d,
       TermMonths = @tm,
       Note = @note,
       PaymentDay = @pd,
       SchedulePath = @sp
 WHERE Id = @id AND UserId = @u;"
                : @"
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

            if (hasSchedule)
                cmd.Parameters.AddWithValue("@sp", (object?)loan.SchedulePath ?? DBNull.Value);

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
            return obj == null || obj == DBNull.Value ? null : Convert.ToInt32(obj);
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

            bool hasSort = ColumnExists(c, "BankAccounts", "SortOrder");

            using var cmd = c.CreateCommand();
            cmd.CommandText = hasSort
                ? @"
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
ORDER BY COALESCE(SortOrder, 999999) ASC, AccountName COLLATE NOCASE ASC;"
                : @"
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
ORDER BY AccountName COLLATE NOCASE ASC;";

            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BankAccountModel
                {
                    Id = r.GetInt32(0),
                    UserId = r.GetInt32(1),
                    ConnectionId = r.IsDBNull(2) ? null : r.GetInt32(2),
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

            bool hasSort = ColumnExists(c, "BankAccounts", "SortOrder");

            using var cmd = c.CreateCommand();

            if (hasSort)
            {
                cmd.CommandText = @"
INSERT INTO BankAccounts(UserId, ConnectionId, BankName, AccountName, Iban, Currency, Balance, SortOrder)
VALUES (
 @u, @conn, @bank, @name, @iban, @cur, @bal,
 (SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM BankAccounts WHERE UserId=@u)
);
SELECT last_insert_rowid();";
            }
            else
            {
                cmd.CommandText = @"
INSERT INTO BankAccounts(UserId, ConnectionId, BankName, AccountName, Iban, Currency, Balance)
VALUES (@u, @conn, @bank, @name, @iban, @cur, @bal);
SELECT last_insert_rowid();";
            }

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

        public static void SaveBankAccountsOrder(int userId, IReadOnlyList<int> orderedAccountIds)
        {
            if (userId <= 0) return;
            if (orderedAccountIds == null) throw new ArgumentNullException(nameof(orderedAccountIds));

            using var c = OpenAndEnsureSchema();

            if (!ColumnExists(c, "BankAccounts", "SortOrder"))
                return; // stara baza bez migracji

            using var tx = c.BeginTransaction();

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE BankAccounts SET SortOrder=@o WHERE Id=@id AND UserId=@u;";

            var pOrder = cmd.CreateParameter(); pOrder.ParameterName = "@o"; cmd.Parameters.Add(pOrder);
            var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; cmd.Parameters.Add(pId);
            var pU = cmd.CreateParameter(); pU.ParameterName = "@u"; pU.Value = userId; cmd.Parameters.Add(pU);

            for (int i = 0; i < orderedAccountIds.Count; i++)
            {
                pOrder.Value = i;
                pId.Value = orderedAccountIds[i];
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            RaiseDataChanged();
        }

        public static void SaveGoalsOrder(int userId, IReadOnlyList<int> orderedEnvelopeIds)
        {
            if (userId <= 0) return;
            if (orderedEnvelopeIds == null) throw new ArgumentNullException(nameof(orderedEnvelopeIds));

            using var c = OpenAndEnsureSchema();

            if (!ColumnExists(c, "Envelopes", "GoalSortOrder"))
                return;

            using var tx = c.BeginTransaction();

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Envelopes SET GoalSortOrder=@o WHERE Id=@id AND UserId=@u;";

            var pOrder = cmd.CreateParameter(); pOrder.ParameterName = "@o"; cmd.Parameters.Add(pOrder);
            var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; cmd.Parameters.Add(pId);
            var pU = cmd.CreateParameter(); pU.ParameterName = "@u"; pU.Value = userId; cmd.Parameters.Add(pU);

            for (int i = 0; i < orderedEnvelopeIds.Count; i++)
            {
                pOrder.Value = i;
                pId.Value = orderedEnvelopeIds[i];
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            RaiseDataChanged();
        }

        public static void SaveEnvelopesOrder(int userId, IReadOnlyList<int> orderedEnvelopeIds)
        {
            if (userId <= 0) return;
            if (orderedEnvelopeIds == null) throw new ArgumentNullException(nameof(orderedEnvelopeIds));

            using var c = OpenAndEnsureSchema();

            if (!ColumnExists(c, "Envelopes", "SortOrder"))
                return;

            using var tx = c.BeginTransaction();

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Envelopes SET SortOrder=@o WHERE Id=@id AND UserId=@u;";

            var pOrder = cmd.CreateParameter(); pOrder.ParameterName = "@o"; cmd.Parameters.Add(pOrder);
            var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; cmd.Parameters.Add(pId);
            var pU = cmd.CreateParameter(); pU.ParameterName = "@u"; pU.Value = userId; cmd.Parameters.Add(pU);

            for (int i = 0; i < orderedEnvelopeIds.Count; i++)
            {
                pOrder.Value = i;
                pId.Value = orderedEnvelopeIds[i];
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            RaiseDataChanged();
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
            bool hasSort = ColumnExists(c, "Envelopes", "SortOrder");

            cmd.CommandText = hasSort
                ? @"
SELECT Id, UserId, Name, Target, Allocated, Note, CreatedAt
FROM Envelopes
WHERE UserId=@u
ORDER BY COALESCE(SortOrder, 999999) ASC, Name COLLATE NOCASE ASC;"
                : @"
SELECT Id, UserId, Name, Target, Allocated, Note, CreatedAt
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

            bool hasGoalSort = ColumnExists(c, "Envelopes", "GoalSortOrder");

            sql += @"
FROM Envelopes
WHERE UserId = @u
  AND Target IS NOT NULL
  AND Target > 0
";

            sql += hasGoalSort
                ? "ORDER BY COALESCE(GoalSortOrder, 999999) ASC, Name COLLATE NOCASE ASC;"
                : "ORDER BY Name COLLATE NOCASE ASC;";


            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // Kolumny:
                // 0 Id, 1 Name, 2 Target, 3 Allocated, 4 Deadline/NULL, 5 GoalText/Note
                var dto = new EnvelopeGoalDto
                {
                    EnvelopeId = r.GetInt32(0),
                    Name = GetStringSafe(r, 1),
                    Target = r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2)),
                    Allocated = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
                    Deadline = null,
                    GoalText = r.IsDBNull(5) ? null : r.GetValue(5)?.ToString()
                };

                // 1) Deadline z kolumny (jeœli istnieje)
                if (hasDeadline)
                {
                    var dt = GetDate(r, 4);
                    dto.Deadline = dt == DateTime.MinValue ? null : dt;
                }

                // 2) Jeœli brak deadline w kolumnie, próbuj wyci¹gn¹æ z NOTE/GoalText
                if (dto.Deadline == null && !string.IsNullOrWhiteSpace(dto.GoalText))
                {
                    dto.Deadline = TryParseDeadlineFromGoalText(dto.GoalText);
                }

                // 3) Tytu³ celu musi istnieæ (Cel: ...)
                var goalTitle = TryParseGoalTitleFromGoalText(dto.GoalText);

                // 4) Do zak³adki „Cele” wpuszczamy TYLKO:
                //    - ma Cel: ...
                //    - ma Termin (deadline)
                if (!string.IsNullOrWhiteSpace(goalTitle) && dto.Deadline != null)
                {
                    list.Add(dto);
                }
            }

            return list;
        }


        private static string? TryParseGoalTitleFromGoalText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line.StartsWith("Cel:", StringComparison.OrdinalIgnoreCase))
                {
                    var title = line.Substring(4).Trim();
                    return string.IsNullOrWhiteSpace(title) ? null : title;
                }
            }

            return null;
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
            string? goalText,
            string? newEnvelopeName = null)
        {
            using var c = OpenAndEnsureSchema();

            bool hasDeadline = ColumnExists(c, "Envelopes", "Deadline");
            bool hasGoalText = ColumnExists(c, "Envelopes", "GoalText");

            var sb = new StringBuilder(@"
UPDATE Envelopes
   SET Target   = @t,
       Allocated = @a");

            // NOWE: jeœli zmieniamy tytu³ celu, to zmieniamy te¿ nazwê koperty
            if (!string.IsNullOrWhiteSpace(newEnvelopeName))
                sb.Append(", Name = @n");

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

            if (!string.IsNullOrWhiteSpace(newEnvelopeName))
                cmd.Parameters.AddWithValue("@n", newEnvelopeName.Trim());

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
            public decimal Cash { get; set; }          // wolna gotówka
            public decimal Saved { get; set; }         // od³o¿ona gotówka (pula)
            public decimal Envelopes { get; set; }     // alokacja w kopertach (czêœæ Saved)
            public decimal Investments { get; set; }   // SUM(CurrentAmount) z Investments

            // opcjonalnie (jeœli masz kafelek "Ca³y maj¹tek")
            public decimal SavedUnallocated => Math.Max(0m, Saved - Envelopes);


            public decimal Total => Banks + Cash + Saved + Investments;
        }


        public static MoneySnapshot GetMoneySnapshot(int userId)
        {
            // Banki
            var banksTotal = GetTotalBanksBalance(userId);

            // CA£A gotówka w portfelu (free + saved)
            var cashOnHandTotal = GetCashOnHand(userId);

            // Od³o¿ona gotówka (pula), z której alokujesz koperty
            var savedTotal = GetSavedCash(userId);

            // Koperty to alokacja z Saved (nie dodawaj do Total jako "nowe pieni¹dze")
            var envelopesAllocated = GetTotalAllocatedInEnvelopesForUser(userId);

            // Inwestycje (SUM(CurrentAmount))
            // Inwestycje (SUM ostatnich wycen)
            var investmentsTotal = GetInvestmentsTotalFromValuations(userId);


            // Wolna gotówka = cash total - saved total
            var freeCash = cashOnHandTotal - savedTotal;
            if (freeCash < 0m) freeCash = 0m;

            return new MoneySnapshot
            {
                Banks = banksTotal,
                Cash = freeCash,
                Saved = savedTotal,
                Envelopes = envelopesAllocated,
                Investments = investmentsTotal
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

            bool hasPk = ColumnExists(con, "Expenses", "PaymentKind");
            bool hasPr = ColumnExists(con, "Expenses", "PaymentRefId");

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
    e.IsPlanned,
    " + (hasPk ? "e.PaymentKind" : "0") + @" AS PaymentKind,
    " + (hasPr ? "e.PaymentRefId" : "NULL") + @" AS PaymentRefId
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

        public static void DeleteTransfer(int id)
        {
            if (id <= 0) return;

            // Najlepiej delegowaæ do fasady, bo ona odwraca salda i ma logikê “planned”
            try
            {
                TransactionsFacadeService.DeleteTransaction(id);
            }
            catch
            {
                // awaryjnie: jeœli coœ pójdzie nie tak, nie wysypuj UI
            }
        }

        public static decimal GetInvestmentsTotal(int userId)
        {
            if (userId <= 0) return 0m;

            using var con = OpenAndEnsureSchema();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT COALESCE(SUM(CurrentAmount), 0)
FROM Investments
WHERE UserId = @uid;";
            cmd.Parameters.AddWithValue("@uid", userId);

            var result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value) return 0m;

            // SQLite mo¿e zwróciæ long/double/string – Convert.ToDecimal to ogarnie
            return Convert.ToDecimal(result, CultureInfo.InvariantCulture);
        }

        public static decimal GetInvestmentsTotalFromValuations(int userId)
        {
            if (userId <= 0) return 0m;

            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();

            // 1 rekord na InvestmentId:
            // - najpierw wybieramy MAX(Date) dla inwestycji
            // - potem w ramach tej daty wybieramy MAX(Id) (tie-breaker)
            // - finalnie sumujemy tylko te rekordy
            cmd.CommandText = @"
SELECT COALESCE(SUM(v.Value), 0)
FROM InvestmentValuations v
JOIN (
    SELECT vv.InvestmentId, MAX(vv.Id) AS MaxId
    FROM InvestmentValuations vv
    JOIN (
        SELECT InvestmentId, MAX(Date) AS MaxDate
        FROM InvestmentValuations
        WHERE UserId = @uid
        GROUP BY InvestmentId
    ) md
    ON md.InvestmentId = vv.InvestmentId
    AND md.MaxDate = vv.Date
    WHERE vv.UserId = @uid
    GROUP BY vv.InvestmentId
) pick
ON pick.MaxId = v.Id
WHERE v.UserId = @uid;
";
            cmd.Parameters.AddWithValue("@uid", userId);

            var result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value) return 0m;

            return Convert.ToDecimal(result, CultureInfo.InvariantCulture);
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

        public static int InsertExpense(Expense e)
        {
            using var c = OpenAndEnsureSchema();

            bool hasAccountId = ExpensesHasAccountId(c);
            bool hasAccountText = ColumnExists(c, "Expenses", "Account"); // legacy
            bool hasBudgetId = ExpensesHasBudgetId(c);

            bool hasPaymentKind = ColumnExists(c, "Expenses", "PaymentKind");
            bool hasPaymentRefId = ColumnExists(c, "Expenses", "PaymentRefId");

            using var tx = c.BeginTransaction();

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;

            var cols = new List<string> { "UserId", "Amount", "Date", "Description", "CategoryId", "IsPlanned" };
            var vals = new List<string> { "@u", "@a", "@d", "@desc", "@c", "@planned" };

            if (hasAccountId) { cols.Add("AccountId"); vals.Add("@accId"); }
            if (hasAccountText) { cols.Add("Account"); vals.Add("@accText"); }
            if (hasBudgetId) { cols.Add("BudgetId"); vals.Add("@budgetId"); }

            // Stabilne ksiêgowanie
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

            // Legacy AccountId: dla kompatybilnoœci mo¿esz trzymaæ AccountId tylko dla banku
            int? legacyAccountId = null;
            if (hasAccountId && e.PaymentKind == Finly.Models.PaymentKind.BankAccount && e.PaymentRefId.HasValue)
                legacyAccountId = e.PaymentRefId.Value;

            if (hasAccountId) cmd.Parameters.AddWithValue("@accId", (object?)legacyAccountId ?? DBNull.Value);
            if (hasAccountText) cmd.Parameters.AddWithValue("@accText", (object?)e.Account ?? DBNull.Value);
            if (hasBudgetId) cmd.Parameters.AddWithValue("@budgetId", (object?)e.BudgetId ?? DBNull.Value);

            if (hasPaymentKind) cmd.Parameters.AddWithValue("@pk", (int)e.PaymentKind);
            if (hasPaymentRefId) cmd.Parameters.AddWithValue("@pr", (object?)e.PaymentRefId ?? DBNull.Value);

            var rowId = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);

            // Ksiêgowanie: TYLKO raz, TYLKO dla nieplanowanych, w tej samej transakcji i tym samym po³¹czeniu
            if (!e.IsPlanned)
            {
                var amt = Convert.ToDecimal(e.Amount);
                LedgerService.ApplyExpenseEffect(c, tx, e.UserId, amt, (int)e.PaymentKind, e.PaymentRefId);
            }

            tx.Commit();
            RaiseDataChanged();
            return rowId;
        }



        public static void UpdateExpense(Expense e)
        {
            using var c = OpenAndEnsureSchema();

            bool hasAccountId = ExpensesHasAccountId(c);
            bool hasAccountText = ColumnExists(c, "Expenses", "Account"); // legacy
            bool hasBudgetId = ExpensesHasBudgetId(c);

            bool hasPaymentKind = ColumnExists(c, "Expenses", "PaymentKind");
            bool hasPaymentRefId = ColumnExists(c, "Expenses", "PaymentRefId");

            using var tx = c.BeginTransaction();

            // 1) Pobierz STARY rekord (do revert)
            var oldInfo = ReadExpenseLedgerInfo(c, tx, e.Id);
            if (oldInfo == null)
                throw new InvalidOperationException("Nie znaleziono wydatku do aktualizacji.");

            // 2) Revert starego wp³ywu (jeœli by³ zrealizowany)
            if (!oldInfo.IsPlanned)
            {
                LedgerService.RevertExpenseEffect(c, tx, oldInfo.UserId, oldInfo.Amount, oldInfo.PaymentKind, oldInfo.PaymentRefId);
            }

            // 3) Ustal NOWE PaymentKind/Ref (zapisujemy i ksiêgujemy spójnie)
            int newPk;
            int? newPr;

            if (hasPaymentKind)
            {
                newPk = (int)e.PaymentKind;      // Finly.Models.PaymentKind
                newPr = e.PaymentRefId;
            }
            else
            {
                // legacy fallback
                int? legacyAccId = null;

                if (hasAccountId || hasAccountText)
                    legacyAccId = TryResolveAccountIdFromExpenseAccountText(c, tx, e.UserId, e.Account);

                newPk = legacyAccId.HasValue
                    ? (int)Finly.Models.PaymentKind.BankAccount
                    : (int)Finly.Models.PaymentKind.FreeCash;

                newPr = legacyAccId;
            }


            // 4) Update rekordu w TEJ SAMEJ transakcji (w tym PaymentKind/PaymentRefId jeœli istniej¹)
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

            if (hasPaymentKind) setParts.Add("PaymentKind=@pk");
            if (hasPaymentRefId) setParts.Add("PaymentRefId=@pr");

            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
UPDATE Expenses SET
 {string.Join(",\n ", setParts)}
WHERE Id=@id;";

                cmd.Parameters.AddWithValue("@id", e.Id);
                cmd.Parameters.AddWithValue("@u", e.UserId);
                cmd.Parameters.AddWithValue("@a", e.Amount);
                cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
                cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);

                if (e.CategoryId > 0) cmd.Parameters.AddWithValue("@c", e.CategoryId);
                else cmd.Parameters.AddWithValue("@c", DBNull.Value);

                cmd.Parameters.AddWithValue("@planned", e.IsPlanned ? 1 : 0);

                if (hasAccountId)
                {
                    int? accId = (newPk == (int)Finly.Models.PaymentKind.BankAccount) ? newPr : null;
                    cmd.Parameters.AddWithValue("@accId", (object?)accId ?? DBNull.Value);
                }

                if (hasAccountText) cmd.Parameters.AddWithValue("@accText", (object?)e.Account ?? DBNull.Value);
                if (hasBudgetId) cmd.Parameters.AddWithValue("@budgetId", (object?)e.BudgetId ?? DBNull.Value);

                if (hasPaymentKind) cmd.Parameters.AddWithValue("@pk", newPk);
                if (hasPaymentRefId) cmd.Parameters.AddWithValue("@pr", (object?)newPr ?? DBNull.Value);

                cmd.ExecuteNonQuery();
            }

            // 5) Apply nowego wp³ywu (jeœli nieplanowany)
            if (!e.IsPlanned)
            {
                var amt = Convert.ToDecimal(e.Amount);
                LedgerService.ApplyExpenseEffect(c, tx, e.UserId, amt, newPk, newPr);
            }

            tx.Commit();
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

        internal static void InsertTransferRaw_NoLedger(
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

            bool hasPk = ColumnExists(con, "Incomes", "PaymentKind");
            bool hasPr = ColumnExists(con, "Incomes", "PaymentRefId");

            var sb = new StringBuilder(@"
SELECT i.Id, i.UserId, i.Date, i.Amount, i.Description, i.Source, i.CategoryId,
       c.Name AS CategoryName, i.IsPlanned,
       " + (hasPk ? "i.PaymentKind" : "0") + @" AS PaymentKind,
       " + (hasPr ? "i.PaymentRefId" : "NULL") + @" AS PaymentRefId
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
            int? categoryId,
            string? source,
            string? description,
            bool isPlanned,
            int? budgetId,
            PaymentKind paymentKind,
            int? paymentRefId)
        {
            if (userId <= 0) throw new ArgumentException("Nieprawid³owy userId.", nameof(userId));
            if (amount <= 0m) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));

            EnsureTables();
            using var c = GetConnection();

            // Kompatybilnoœæ ze starszymi DB (kolumny mog¹ nie istnieæ)
            bool hasPaymentKind = ColumnExists(c, "Incomes", "PaymentKind");
            bool hasPaymentRefId = ColumnExists(c, "Incomes", "PaymentRefId");
            bool hasBudgetId = ColumnExists(c, "Incomes", "BudgetId");
            bool hasSource = ColumnExists(c, "Incomes", "Source");
            bool hasDesc = ColumnExists(c, "Incomes", "Description");
            bool hasPlanned = ColumnExists(c, "Incomes", "IsPlanned");
            bool hasCategory = ColumnExists(c, "Incomes", "CategoryId");

            using var tx = c.BeginTransaction();

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;

            var cols = new List<string> { "UserId", "Amount", "Date" };
            var vals = new List<string> { "@u", "@a", "@d" };

            if (hasDesc) { cols.Add("Description"); vals.Add("@desc"); }
            if (hasSource) { cols.Add("Source"); vals.Add("@src"); }
            if (hasCategory) { cols.Add("CategoryId"); vals.Add("@cat"); }

            if (hasPlanned) { cols.Add("IsPlanned"); vals.Add("@p"); }
            if (hasBudgetId) { cols.Add("BudgetId"); vals.Add("@b"); }

            if (hasPaymentKind) { cols.Add("PaymentKind"); vals.Add("@pk"); }
            if (hasPaymentRefId) { cols.Add("PaymentRefId"); vals.Add("@pr"); }

            cmd.CommandText = $@"
INSERT INTO Incomes({string.Join(",", cols)})
VALUES ({string.Join(",", vals)});
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(date));

            if (hasDesc) cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
            if (hasSource) cmd.Parameters.AddWithValue("@src", (object?)source ?? DBNull.Value);

            if (hasCategory)
            {
                if (categoryId.HasValue && categoryId.Value > 0)
                    cmd.Parameters.AddWithValue("@cat", categoryId.Value);
                else
                    cmd.Parameters.AddWithValue("@cat", DBNull.Value);
            }

            if (hasPlanned) cmd.Parameters.AddWithValue("@p", isPlanned ? 1 : 0);
            if (hasBudgetId) cmd.Parameters.AddWithValue("@b", (object?)budgetId ?? DBNull.Value);

            if (hasPaymentKind) cmd.Parameters.AddWithValue("@pk", (int)paymentKind);
            if (hasPaymentRefId) cmd.Parameters.AddWithValue("@pr", (object?)paymentRefId ?? DBNull.Value);

            var rowId = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);

            // Ksiêgowanie WY£¥CZNIE dla nieplanowanych i w tej samej transakcji
            if (!isPlanned)
            {
                LedgerService.ApplyIncomeEffect(
                    c, tx,
                    userId: userId,
                    amount: amount,
                    paymentKind: (int)paymentKind,
                    paymentRefId: paymentRefId
                );
            }

            tx.Commit();
            NotifyDataChanged(); // albo RaiseDataChanged() – u¿yj tego, co masz w DatabaseService
            return rowId;
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
            int? budgetId = null,
            Finly.Models.PaymentKind? paymentKind = null,
            int? paymentRefId = null)
        {
            using var con = OpenAndEnsureSchema();

            bool hasBudgetId = ColumnExists(con, "Incomes", "BudgetId");
            bool hasPk = ColumnExists(con, "Incomes", "PaymentKind");
            bool hasPr = ColumnExists(con, "Incomes", "PaymentRefId");

            using var tx = con.BeginTransaction();

            var old = ReadIncomeLedgerInfo(con, tx, id);
            if (old == null) throw new InvalidOperationException("Nie znaleziono przychodu do aktualizacji.");

            // 1) revert starego wp³ywu (jeœli by³ zrealizowany)
            if (!old.IsPlanned)
            {
                LedgerService.RevertIncomeEffect(con, tx, old.UserId, Math.Abs(old.Amount), old.PaymentKind, old.PaymentRefId);
            }

            // 2) policz nowe wartoœci (fallback: jeœli null -> zostaw stare)
            var newAmount = amount ?? old.Amount;
            var newPlanned = isPlanned ?? old.IsPlanned;

            int newPk = hasPk ? (int)(paymentKind ?? (Finly.Models.PaymentKind)old.PaymentKind) : old.PaymentKind;
            int? newPr = hasPr ? (paymentKind.HasValue ? paymentRefId : old.PaymentRefId) : old.PaymentRefId;

            // 3) update rekord
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
            if (hasPk) setParts.Add("PaymentKind = COALESCE(@pk, PaymentKind)");
            if (hasPr) setParts.Add("PaymentRefId = @pr");

            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"UPDATE Incomes SET {string.Join(", ", setParts)} WHERE Id=@id AND UserId=@u;";

                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@a", (object?)amount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@p", isPlanned.HasValue ? (isPlanned.Value ? 1 : 0) : DBNull.Value);
                cmd.Parameters.AddWithValue("@d", date.HasValue ? date.Value.ToString("yyyy-MM-dd") : DBNull.Value);

                if (categoryId.HasValue && categoryId.Value > 0) cmd.Parameters.AddWithValue("@c", categoryId.Value);
                else cmd.Parameters.AddWithValue("@c", DBNull.Value);

                cmd.Parameters.AddWithValue("@s", (object?)source ?? DBNull.Value);

                if (hasBudgetId) cmd.Parameters.AddWithValue("@b", (object?)budgetId ?? DBNull.Value);

                if (hasPk) cmd.Parameters.AddWithValue("@pk", paymentKind.HasValue ? (int)paymentKind.Value : DBNull.Value);
                if (hasPr) cmd.Parameters.AddWithValue("@pr", hasPr ? (object?)newPr ?? DBNull.Value : DBNull.Value);

                cmd.ExecuteNonQuery();
            }

            // 4) apply nowego wp³ywu (jeœli nowe jest zrealizowane)
            if (!newPlanned)
            {
                LedgerService.ApplyIncomeEffect(con, tx, userId, Math.Abs(newAmount), newPk, newPr);
            }

            tx.Commit();
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


        // =========================================================
        // ======================= INVESTMENTS ======================
        // =========================================================

        private static decimal ReadDecimal(SqliteDataReader r, int ordinal)
        {
            if (r.IsDBNull(ordinal)) return 0m;

            var v = r.GetValue(ordinal);

            // SQLite potrafi zwróciæ: long, double, string, decimal…
            return v switch
            {
                decimal d => d,
                double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
                float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
                long l => l,
                int i => i,
                short s => s,
                byte b => b,
                string s => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                                ? d
                                : (decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out d) ? d : 0m),
                _ => Convert.ToDecimal(v, CultureInfo.InvariantCulture)
            };
        }

        private static int ReadInt32(SqliteDataReader r, int ordinal, int fallback = 0)
            => r.IsDBNull(ordinal) ? fallback : r.GetInt32(ordinal);

        private static string? ReadString(SqliteDataReader r, int ordinal)
            => r.IsDBNull(ordinal) ? null : r.GetString(ordinal);


        public static List<InvestmentModel> GetInvestments(int userId)
        {
            var list = new List<InvestmentModel>();
            if (userId <= 0) return list;

            using var c = OpenAndEnsureSchema();

            // jeœli ktoœ ma star¹ bazê bez kolumny Type – fallback na 0
            bool hasType = ColumnExists(c, "Investments", "Type");

            using var cmd = c.CreateCommand();
            cmd.CommandText = $@"
SELECT Id, UserId, Name,
       {(hasType ? "Type" : "0")} AS Type,
       TargetAmount, CurrentAmount, TargetDate, Description
FROM Investments
WHERE UserId = @u
ORDER BY Id DESC;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new InvestmentModel
                {
                    Id = ReadInt32(r, 0),
                    UserId = ReadInt32(r, 1, userId),
                    Name = ReadString(r, 2) ?? string.Empty,
                    Type = (InvestmentType)ReadInt32(r, 3, 0),
                    TargetAmount = ReadDecimal(r, 4),
                    CurrentAmount = ReadDecimal(r, 5),
                    TargetDate = ReadString(r, 6),
                    Description = ReadString(r, 7)
                });
            }

            return list;
        }

        public static int InsertInvestment(InvestmentModel m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (m.UserId <= 0) throw new ArgumentException("Nieprawid³owy UserId.", nameof(m.UserId));

            using var c = OpenAndEnsureSchema();
            bool hasType = ColumnExists(c, "Investments", "Type");

            using var cmd = c.CreateCommand();
            cmd.CommandText = hasType
                ? @"
INSERT INTO Investments(UserId, Name, Type, TargetAmount, CurrentAmount, TargetDate, Description)
VALUES (@u,@n,@type,@tgt,@cur,@d,@desc);
SELECT last_insert_rowid();"
                : @"
INSERT INTO Investments(UserId, Name, TargetAmount, CurrentAmount, TargetDate, Description)
VALUES (@u,@n,@tgt,@cur,@d,@desc);
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@u", m.UserId);
            cmd.Parameters.AddWithValue("@n", (m.Name ?? string.Empty).Trim());

            if (hasType) cmd.Parameters.AddWithValue("@type", (int)m.Type);

            cmd.Parameters.AddWithValue("@tgt", m.TargetAmount);
            cmd.Parameters.AddWithValue("@cur", m.CurrentAmount);
            cmd.Parameters.AddWithValue("@d", (object?)m.TargetDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", (object?)m.Description ?? DBNull.Value);

            var raw = cmd.ExecuteScalar();
            var id = Convert.ToInt32(raw ?? 0, CultureInfo.InvariantCulture);

            // Odœwie¿enia UI
            RaiseDataChanged();

            return id;
        }

        public static void UpdateInvestment(InvestmentModel m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (m.UserId <= 0) throw new ArgumentException("Nieprawid³owy UserId.", nameof(m.UserId));
            if (m.Id <= 0) throw new ArgumentException("Nieprawid³owe Id inwestycji.", nameof(m.Id));

            using var c = OpenAndEnsureSchema();
            bool hasType = ColumnExists(c, "Investments", "Type");

            using var cmd = c.CreateCommand();
            cmd.CommandText = hasType
                ? @"
UPDATE Investments
SET Name=@n, Type=@type, TargetAmount=@tgt, CurrentAmount=@cur, TargetDate=@d, Description=@desc
WHERE Id=@id AND UserId=@u;"
                : @"
UPDATE Investments
SET Name=@n, TargetAmount=@tgt, CurrentAmount=@cur, TargetDate=@d, Description=@desc
WHERE Id=@id AND UserId=@u;";

            cmd.Parameters.AddWithValue("@id", m.Id);
            cmd.Parameters.AddWithValue("@u", m.UserId);
            cmd.Parameters.AddWithValue("@n", (m.Name ?? string.Empty).Trim());

            if (hasType) cmd.Parameters.AddWithValue("@type", (int)m.Type);

            cmd.Parameters.AddWithValue("@tgt", m.TargetAmount);
            cmd.Parameters.AddWithValue("@cur", m.CurrentAmount);
            cmd.Parameters.AddWithValue("@d", (object?)m.TargetDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", (object?)m.Description ?? DBNull.Value);

            var affected = cmd.ExecuteNonQuery();
            if (affected <= 0)
                throw new InvalidOperationException("Nie zaktualizowano inwestycji (brak rekordu lub brak uprawnieñ).");

            RaiseDataChanged();
        }

        public static void DeleteInvestment(int userId, int investmentId)
        {
            if (userId <= 0 || investmentId <= 0) return;

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Investments WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", investmentId);
            cmd.Parameters.AddWithValue("@u", userId);

            var affected = cmd.ExecuteNonQuery();
            if (affected <= 0)
                throw new InvalidOperationException("Nie usuniêto inwestycji (brak rekordu lub brak uprawnieñ).");

            // U Ciebie tego brakowa³o – przez to dashboard/kafelki mog³y nie odœwie¿aæ
            RaiseDataChanged();
        }




        private sealed class ExpenseLedgerInfo
        {
            public int UserId { get; set; }
            public decimal Amount { get; set; }
            public bool IsPlanned { get; set; }
            public int PaymentKind { get; set; }
            public int? PaymentRefId { get; set; }
        }

        private static ExpenseLedgerInfo? ReadExpenseLedgerInfo(SqliteConnection c, SqliteTransaction tx, int expenseId)
        {
            if (!TableExists(c, "Expenses")) return null;

            bool hasIsPlanned = ColumnExists(c, "Expenses", "IsPlanned");
            bool hasPk = ColumnExists(c, "Expenses", "PaymentKind");
            bool hasPr = ColumnExists(c, "Expenses", "PaymentRefId");
            bool hasAccountId = ColumnExists(c, "Expenses", "AccountId");
            bool hasAccountText = ColumnExists(c, "Expenses", "Account"); // legacy

            // Minimalny SELECT po to, co potrzebne.
            // Jeœli brak PaymentKind -> legacy fallback: BankAccount jeœli AccountId jest, inaczej FreeCash.
            string sql = @"
SELECT UserId,
       Amount,
       " + (hasIsPlanned ? "IsPlanned" : "0") + @" AS IsPlanned,
       " + (hasPk ? "PaymentKind" : "NULL") + @" AS PaymentKind,
       " + (hasPr ? "PaymentRefId" : "NULL") + @" AS PaymentRefId,
       " + (hasAccountId ? "AccountId" : "NULL") + @" AS AccountId,
       " + (hasAccountText ? "Account" : "NULL") + @" AS AccountText
FROM Expenses
WHERE Id=@id
LIMIT 1;";

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", expenseId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            int userId = r.IsDBNull(0) ? 0 : r.GetInt32(0);
            decimal amount = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
            bool isPlanned = !r.IsDBNull(2) && Convert.ToInt32(r.GetValue(2)) == 1;

            int? pk = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetValue(3));
            int? pr = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4));

            int? accountId = r.IsDBNull(5) ? (int?)null : Convert.ToInt32(r.GetValue(5));
            string? accountText = r.IsDBNull(6) ? null : r.GetValue(6)?.ToString();

            if (pk.HasValue)
            {
                return new ExpenseLedgerInfo
                {
                    UserId = userId,
                    Amount = amount,
                    IsPlanned = isPlanned,
                    PaymentKind = pk.Value,
                    PaymentRefId = pr
                };
            }

            // LEGACY fallback (spójny, choæ nie obs³u¿y kopert/saved w starych bazach):
            // Jeœli mamy AccountId -> traktuj jako BankAccount, wpp FreeCash.
            int legacyKind = accountId.HasValue
    ? (int)Finly.Models.PaymentKind.BankAccount
    : (int)Finly.Models.PaymentKind.FreeCash;

            int? legacyRef = accountId;

            // Dodatkowo: jeœli AccountId nie ma, ale jest tekst "Konto: ..." -> spróbuj zmapowaæ na bank
            if (!legacyRef.HasValue && hasAccountText && !string.IsNullOrWhiteSpace(accountText))
            {
                var resolved = TryResolveAccountIdFromExpenseAccountText(c, tx, userId, accountText);
                if (resolved.HasValue)
                {
                    legacyKind = (int)Finly.Models.PaymentKind.BankAccount;
                    legacyRef = resolved;
                }
            }

            return new ExpenseLedgerInfo
            {
                UserId = userId,
                Amount = amount,
                IsPlanned = isPlanned,
                PaymentKind = legacyKind,
                PaymentRefId = legacyRef
            };
        }

        private sealed class IncomeLedgerInfo
        {
            public int UserId { get; set; }
            public decimal Amount { get; set; }
            public bool IsPlanned { get; set; }
            public int PaymentKind { get; set; }
            public int? PaymentRefId { get; set; }
        }

        private static IncomeLedgerInfo? ReadIncomeLedgerInfo(SqliteConnection c, SqliteTransaction tx, int incomeId)
        {
            if (!TableExists(c, "Incomes")) return null;

            bool hasIsPlanned = ColumnExists(c, "Incomes", "IsPlanned");
            bool hasPk = ColumnExists(c, "Incomes", "PaymentKind");
            bool hasPr = ColumnExists(c, "Incomes", "PaymentRefId");

            string sql = @"
SELECT UserId,
       Amount,
       " + (hasIsPlanned ? "IsPlanned" : "0") + @" AS IsPlanned,
       " + (hasPk ? "PaymentKind" : "NULL") + @" AS PaymentKind,
       " + (hasPr ? "PaymentRefId" : "NULL") + @" AS PaymentRefId
FROM Incomes
WHERE Id=@id
LIMIT 1;";

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", incomeId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            int userId = r.IsDBNull(0) ? 0 : r.GetInt32(0);
            decimal amount = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
            bool isPlanned = !r.IsDBNull(2) && Convert.ToInt32(r.GetValue(2)) == 1;

            // Jeœli brak kolumn PaymentKind/RefId w starej bazie:
            // przychody traktujemy jako FreeCash (0 w Twoich SELECT-ach).
            int pk = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
            int? pr = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4));

            return new IncomeLedgerInfo
            {
                UserId = userId,
                Amount = amount,
                IsPlanned = isPlanned,
                PaymentKind = pk,
                PaymentRefId = pr
            };
        }


        public static void EnsureDefaultCategories(int userId)
        {
            if (userId <= 0) return;

            using var c = OpenAndEnsureSchema();

            // uwzglêdnij archiwizacjê jeœli kolumna istnieje
            bool hasArchived = ColumnExists(c, "Categories", "IsArchived");
            bool hasType = ColumnExists(c, "Categories", "Type");

            // Jeœli u¿ytkownik ma ju¿ jakiekolwiek (niearchiwalne) kategorie, nie dok³adamy domyœlnych
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = hasArchived
                    ? "SELECT COUNT(*) FROM Categories WHERE UserId=@u AND IsArchived=0;"
                    : "SELECT COUNT(*) FROM Categories WHERE UserId=@u;";
                chk.Parameters.AddWithValue("@u", userId);

                var count = Convert.ToInt32(chk.ExecuteScalar() ?? 0);
                if (count > 0) return;
            }

            // Minimalny, sensowny zestaw startowy
            var defaultsExpense = new[]
            {
        "Jedzenie",
        "Dom",
        "Transport",
        "Zdrowie",
        "Ubrania",
        "Rozrywka",
        "Rachunki",
        "Inne"
    };

            var defaultsIncome = new[]
            {
        "Wyp³ata",
        "Premia",
        "Zwrot",
        "Inne"
    };

            using var tx = c.BeginTransaction();

            void Insert(string name, int type)
            {
                using var ins = c.CreateCommand();
                ins.Transaction = tx;

                if (hasType)
                {
                    ins.CommandText = @"
INSERT OR IGNORE INTO Categories(UserId, Name, Type)
VALUES(@u, @n, @t);";
                    ins.Parameters.AddWithValue("@t", type);
                }
                else
                {
                    ins.CommandText = @"
INSERT OR IGNORE INTO Categories(UserId, Name)
VALUES(@u, @n);";
                }

                ins.Parameters.AddWithValue("@u", userId);
                ins.Parameters.AddWithValue("@n", name);
                ins.ExecuteNonQuery();
            }


            // Domyœlne
            foreach (var n in defaultsExpense) Insert(n, 0); // Expense
            foreach (var n in defaultsIncome) Insert(n, 1); // Income


            tx.Commit();

            RaiseDataChanged();
        }

        public static void UpdatePlannedExpenseDate(int userId, int expenseId, DateTime newDate)
        {
            if (userId <= 0 || expenseId <= 0) return;

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            bool hasIsPlanned = ColumnExists(c, "Expenses", "IsPlanned");

            cmd.CommandText = hasIsPlanned
                ? @"UPDATE Expenses
              SET Date=@d
            WHERE Id=@id AND UserId=@u AND IsPlanned=1;"
                : @"UPDATE Expenses
              SET Date=@d
            WHERE Id=@id AND UserId=@u;";

            cmd.Parameters.AddWithValue("@d", ToIsoDate(newDate));
            cmd.Parameters.AddWithValue("@id", expenseId);
            cmd.Parameters.AddWithValue("@u", userId);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void UpdatePlannedIncomeDate(int userId, int incomeId, DateTime newDate)
        {
            if (userId <= 0 || incomeId <= 0) return;

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            bool hasIsPlanned = ColumnExists(c, "Incomes", "IsPlanned");

            cmd.CommandText = hasIsPlanned
                ? @"UPDATE Incomes
              SET Date=@d
            WHERE Id=@id AND UserId=@u AND IsPlanned=1;"
                : @"UPDATE Incomes
              SET Date=@d
            WHERE Id=@id AND UserId=@u;";

            cmd.Parameters.AddWithValue("@d", ToIsoDate(newDate));
            cmd.Parameters.AddWithValue("@id", incomeId);
            cmd.Parameters.AddWithValue("@u", userId);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        public static void UpdatePlannedTransferDate(int userId, int transferId, DateTime newDate)
        {
            if (userId <= 0 || transferId <= 0) return;

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            bool hasIsPlanned = ColumnExists(c, "Transfers", "IsPlanned");

            cmd.CommandText = hasIsPlanned
                ? @"UPDATE Transfers
              SET Date=@d
            WHERE Id=@id AND UserId=@u AND IsPlanned=1;"
                : @"UPDATE Transfers
              SET Date=@d
            WHERE Id=@id AND UserId=@u;";

            cmd.Parameters.AddWithValue("@d", ToIsoDate(newDate));
            cmd.Parameters.AddWithValue("@id", transferId);
            cmd.Parameters.AddWithValue("@u", userId);

            cmd.ExecuteNonQuery();
            RaiseDataChanged();
        }

        private static int? TryResolveAccountIdFromExpenseAccountText(SqliteConnection c, SqliteTransaction tx, int userId, string? accountText)
        {
            if (string.IsNullOrWhiteSpace(accountText)) return null;

            var t = accountText.Trim();
            if (!t.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase)) return null;

            var name = t.Substring("Konto:".Length).Trim();
            if (string.IsNullOrWhiteSpace(name)) return null;

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT Id FROM BankAccounts WHERE UserId=@u AND AccountName=@n LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name);
            var obj = cmd.ExecuteScalar();
            return obj == null || obj == DBNull.Value ? null : Convert.ToInt32(obj);
        }


    }
}
