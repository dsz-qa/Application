using Finly.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;

namespace Finly.Services
{
    /// Centralny serwis SQLite – spójny z wywo³aniami w UI.
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

            // FK musz¹ byæ W£¥CZONE na KA¯DYM po³¹czeniu
            using var pragma = con.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();

            return con;
        }

        public static void EnsureTables()
        {
            lock (_schemaLock)
            {
                if (_schemaInitialized) return;
                using var c = GetConnection();
                SchemaService.Ensure(c);
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
                        _schemaInitialized = true;
                    }
                }
            }
            return GetConnection();
        }

        private static string ToIsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd");

        // ==== helpery odczytu z DataReadera ====
        private static string? GetNullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        private static string GetStringSafe(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        private static DateTime GetDate(SqliteDataReader r, int i)
        {
            if (r.IsDBNull(i)) return DateTime.MinValue;
            var v = r.GetValue(i);
            if (v is DateTime dt) return dt;
            return DateTime.TryParse(v?.ToString(), out var p) ? p : DateTime.MinValue;
        }

        // =========================================================
        // ======================= KATEGORIE =======================
        // =========================================================

        public static DataTable GetCategories(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
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
            cmd.CommandText = @"SELECT Id FROM Categories 
                                WHERE UserId=@u AND lower(Name)=lower(@n) LIMIT 1;";
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
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static int GetOrCreateCategoryId(int userId, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nazwa kategorii pusta.", nameof(name));
            var existing = GetCategoryIdByName(userId, name);
            return existing ?? CreateCategory(userId, name);
        }

        // alias zgodnoœci
        public static int GetOrCreateCategoryId(string name, int userId) => GetOrCreateCategoryId(userId, name);

        public static void UpdateCategory(int id, string name)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE Categories SET Name=@n WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@n", name.Trim());
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static void DeleteCategory(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Categories WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // =========================================================
        // ========================= KONTA =========================
        // =========================================================

        /// <summary>
        /// Widok listy rachunków. Kolumna BankName to placeholder (na przysz³oœæ JOIN).
        /// </summary>
        public static DataTable GetAccountsTable(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT 
    Id,
    '' AS BankName,
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
            cmd.CommandText = @"SELECT Id, UserId, ConnectionId, AccountName, Iban, Currency, Balance
                                FROM BankAccounts WHERE UserId=@u ORDER BY AccountName;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BankAccountModel
                {
                    Id = r.GetInt32(0),
                    UserId = r.GetInt32(1),
                    ConnectionId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    AccountName = GetStringSafe(r, 3),
                    Iban = GetStringSafe(r, 4),
                    Currency = string.IsNullOrWhiteSpace(GetStringSafe(r, 5)) ? "PLN" : GetStringSafe(r, 5),
                    Balance = r.IsDBNull(6) ? 0m : Convert.ToDecimal(r.GetValue(6))
                });
            }
            return list;
        }

        public static int InsertAccount(BankAccountModel a)
        {
            using var c = OpenAndEnsureSchema();

            // Je¿eli podano ConnectionId, ale taki rekord nie istnieje – wyzeruj,
            // aby nie z³apaæ FK na INSERT.
            if (a.ConnectionId is int cid)
            {
                using var chk = c.CreateCommand();
                chk.CommandText = "SELECT 1 FROM BankConnections WHERE Id=@cid LIMIT 1;";
                chk.Parameters.AddWithValue("@cid", cid);
                if (chk.ExecuteScalar() is null) a.ConnectionId = null;
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO BankAccounts(UserId, ConnectionId, AccountName, Iban, Currency, Balance)
VALUES (@u, @conn, @name, @iban, @cur, @bal);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", a.UserId);
            cmd.Parameters.AddWithValue("@conn", (object?)a.ConnectionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", a.AccountName ?? "");
            cmd.Parameters.AddWithValue("@iban", a.Iban ?? "");
            cmd.Parameters.AddWithValue("@cur", string.IsNullOrWhiteSpace(a.Currency) ? "PLN" : a.Currency);
            cmd.Parameters.AddWithValue("@bal", a.Balance);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Stabilny UPDATE z ochron¹ przed 'FOREIGN KEY constraint failed'
        /// (sieroty w ConnectionId -> SET NULL).
        /// </summary>
        public static void UpdateAccount(BankAccountModel a)
        {
            using var c = OpenAndEnsureSchema();

            // 1) Napraw ewentualne „sieroty” w istniej¹cym rekordzie
            EnsureValidConnectionId(c, a.Id);

            // 2) Jeœli przychodzi nowe ConnectionId – sprawdŸ, czy istnieje.
            if (a.ConnectionId is int cid)
            {
                using var chk = c.CreateCommand();
                chk.CommandText = "SELECT 1 FROM BankConnections WHERE Id=@cid LIMIT 1;";
                chk.Parameters.AddWithValue("@cid", cid);
                if (chk.ExecuteScalar() is null) a.ConnectionId = null; // miêkkie wyzerowanie
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE BankAccounts SET
    ConnectionId=@conn, AccountName=@name, Iban=@iban, Currency=@cur, Balance=@bal
WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.Parameters.AddWithValue("@u", a.UserId);
            cmd.Parameters.AddWithValue("@conn", (object?)a.ConnectionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", a.AccountName ?? "");
            cmd.Parameters.AddWithValue("@iban", a.Iban ?? "");
            cmd.Parameters.AddWithValue("@cur", string.IsNullOrWhiteSpace(a.Currency) ? "PLN" : a.Currency);
            cmd.Parameters.AddWithValue("@bal", a.Balance);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // FK failed
            {
                // Wyœcig? Zmiêkcz FK do NULL i spróbuj ponownie.
                EnsureValidConnectionId(c, a.Id);
                cmd.Parameters["@conn"].Value = (object?)a.ConnectionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
        }

        public static void DeleteAccount(int id, int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM BankAccounts WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }

        // =========================================================
        // ========================= WYDATKI =======================
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
            var sb = new StringBuilder(@"
SELECT e.Id, e.UserId, e.Date, e.Amount, e.Title, e.Note,
       e.CategoryId, COALESCE(c.Name,'(brak)') AS CategoryName, e.AccountId
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId = @uid");
            cmd.Parameters.AddWithValue("@uid", userId);

            if (from != null) { sb.Append(" AND date(e.Date) >= date(@from)"); cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd")); }
            if (to != null) { sb.Append(" AND date(e.Date) <= date(@to)"); cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd")); }
            if (categoryId != null) { sb.Append(" AND e.CategoryId = @cid"); cmd.Parameters.AddWithValue("@cid", categoryId.Value); }
            if (accountId != null) { sb.Append(" AND e.AccountId  = @acc"); cmd.Parameters.AddWithValue("@acc", accountId.Value); }
            if (!string.IsNullOrWhiteSpace(search))
            {
                sb.Append(" AND (lower(e.Title) LIKE @q OR lower(e.Note) LIKE @q)");
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
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT Id, UserId, Amount, Date, Description, CategoryId
                                FROM Expenses WHERE Id=@id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var catId = r.IsDBNull(5) ? 0 : r.GetInt32(5);

            return new Expense
            {
                Id = r.GetInt32(0),
                UserId = r.GetInt32(1),
                Amount = Convert.ToDouble(r.GetValue(2)),
                Date = GetDate(r, 3),
                Description = GetNullableString(r, 4),
                CategoryId = catId
            };
        }

        public static int InsertExpense(Expense e)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Expenses(UserId, Amount, Date, Description, CategoryId)
VALUES (@u,@a,@d,@desc,@c);
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@u", e.UserId);
            cmd.Parameters.AddWithValue("@a", e.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);

            if (e.CategoryId is int cid && cid > 0)
                cmd.Parameters.AddWithValue("@c", cid);
            else
                cmd.Parameters.AddWithValue("@c", DBNull.Value);

            var rowId = (long)(cmd.ExecuteScalar() ?? 0L);
            return (int)rowId;
        }

        public static void UpdateExpense(Expense e)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE Expenses SET
    UserId=@u, Amount=@a, Date=@d, Description=@desc, CategoryId=@c
WHERE Id=@id;";

            cmd.Parameters.AddWithValue("@id", e.Id);
            cmd.Parameters.AddWithValue("@u", e.UserId);
            cmd.Parameters.AddWithValue("@a", e.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);

            if (e.CategoryId is int cid && cid > 0)
                cmd.Parameters.AddWithValue("@c", cid);
            else
                cmd.Parameters.AddWithValue("@c", DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public static void AddExpense(Expense e)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Expenses(UserId, Amount, Date, Description, CategoryId)
VALUES (@u, @a, @d, @desc, @c);";
            cmd.Parameters.AddWithValue("@u", e.UserId);
            cmd.Parameters.AddWithValue("@a", e.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);

            if (e.CategoryId is int cid && cid > 0)
                cmd.Parameters.AddWithValue("@c", cid);
            else
                cmd.Parameters.AddWithValue("@c", DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public static void DeleteExpense(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Expenses WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ===== FK helper =====

        /// <summary>
        /// Jeœli BankAccounts.ConnectionId wskazuje na nieistniej¹cy rekord – ustawia NULL,
        /// aby nie wywalaæ 'FOREIGN KEY constraint failed' przy UPDATE/DELETE.
        /// </summary>
        private static void EnsureValidConnectionId(SqliteConnection c, int accountId)
        {
            using var get = c.CreateCommand();
            get.CommandText = "SELECT ConnectionId FROM BankAccounts WHERE Id=@id LIMIT 1;";
            get.Parameters.AddWithValue("@id", accountId);
            var raw = get.ExecuteScalar();

            if (raw is null || raw == DBNull.Value) return;  // ju¿ NULL

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
        // ======================== KOPERTY ========================
        // =========================================================

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

            var idObj = cmd.ExecuteScalar();
            return Convert.ToInt32(idObj);
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
        }

        public static void DeleteEnvelope(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Envelopes WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // =========================================================
        // ======================== GOTÓWKA ========================
        // =========================================================

        public static decimal GetCashOnHand(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Amount FROM CashOnHand WHERE UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            var obj = cmd.ExecuteScalar();
            return (obj == null || obj == DBNull.Value) ? 0m : Convert.ToDecimal(obj);
        }

        public static void SetCashOnHand(int userId, decimal amount)
        {
            using var c = OpenAndEnsureSchema();
            using var up = c.CreateCommand();
            // UPSERT – wstawi nowy wiersz lub zaktualizuje istniej¹cy
            up.CommandText = @"
INSERT INTO CashOnHand(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE 
SET Amount=excluded.Amount, UpdatedAt=CURRENT_TIMESTAMP;";
            up.Parameters.AddWithValue("@u", userId);
            up.Parameters.AddWithValue("@a", amount);
            up.ExecuteNonQuery();
        }

        // =========================================================
        // ======================= PODSUMOWANIA ====================
        // =========================================================

        public static decimal GetTotalBanksBalance(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Balance),0) FROM BankAccounts WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar());
        }

        private static decimal GetTotalAllocatedInEnvelopes(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Allocated),0) FROM Envelopes WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar());
        }

        // ======================= PODSUMOWANIA / PRZELEWY =======================

        public sealed class MoneySnapshot
        {
            public decimal Banks { get; set; }
            public decimal Cash { get; set; }
            public decimal Envelopes { get; set; }              // suma przydzielona w kopertach
            public decimal AvailableToAllocate => Cash - Envelopes; // tylko gotówka do podzia³u
            public decimal Total => Banks + Cash;
        }

        public static MoneySnapshot GetMoneySnapshot(int userId)
        {
            var banks = GetTotalBanksBalance(userId);
            var cash = GetCashOnHand(userId);
            var envelopes = GetTotalAllocatedInEnvelopes(userId);

            return new MoneySnapshot
            {
                Banks = banks,
                Cash = cash,
                Envelopes = envelopes
            };
        }

        /// <summary>
        /// Przelew z rachunku bankowego do gotówki.
        /// Zmniejsza saldo konta i zwiêksza CashOnHand — w jednej transakcji.
        /// </summary>
        public static void TransferBankToCash(int userId, int accountId, decimal amount)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

            // SprawdŸ saldo
            decimal currentBal;
            using (var chk = c.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = @"SELECT Balance FROM BankAccounts WHERE Id=@id AND UserId=@u LIMIT 1;";
                chk.Parameters.AddWithValue("@id", accountId);
                chk.Parameters.AddWithValue("@u", userId);
                var obj = chk.ExecuteScalar();
                if (obj == null || obj == DBNull.Value)
                    throw new InvalidOperationException("Nie znaleziono rachunku lub nie nale¿y do u¿ytkownika.");
                currentBal = Convert.ToDecimal(obj);
            }
            if (currentBal < amount)
                throw new InvalidOperationException("Na rachunku brakuje œrodków na tak¹ wyp³atê.");

            // Odejmij z banku
            using (var up = c.CreateCommand())
            {
                up.Transaction = tx;
                up.CommandText = @"UPDATE BankAccounts SET Balance = Balance - @a WHERE Id=@id AND UserId=@u;";
                up.Parameters.AddWithValue("@a", amount);
                up.Parameters.AddWithValue("@id", accountId);
                up.Parameters.AddWithValue("@u", userId);
                up.ExecuteNonQuery();
            }

            // Dodaj do gotówki (UPSERT: kumuluje kwotê)
            using (var cash = c.CreateCommand())
            {
                cash.Transaction = tx;
                cash.CommandText = @"
INSERT INTO CashOnHand(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE
SET Amount = CashOnHand.Amount + excluded.Amount,
    UpdatedAt = CURRENT_TIMESTAMP;";
                cash.Parameters.AddWithValue("@u", userId);
                cash.Parameters.AddWithValue("@a", amount);
                cash.ExecuteNonQuery();
            }

            tx.Commit();

        }


        // == PRZELEWY GOTÓWKA <-> BANK ==

        /// <summary>
        /// Przelew z GOTÓWKI na wskazany rachunek bankowy.
        /// Zmniejsza CashOnHand i zwiêksza saldo rachunku – wszystko w jednej transakcji.
        /// </summary>
        public static void TransferCashToBank(int userId, int accountId, decimal amount)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

            // 1) SprawdŸ aktualn¹ gotówkê
            decimal currentCash;
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT COALESCE(Amount,0) FROM CashOnHand WHERE UserId=@u LIMIT 1;";
                q.Parameters.AddWithValue("@u", userId);
                currentCash = Convert.ToDecimal(q.ExecuteScalar() ?? 0m);
            }
            if (currentCash < amount)
                throw new InvalidOperationException("Za ma³o gotówki na tak¹ wp³atê.");

            // 2) Odejmij z gotówki
            using (var updCash = c.CreateCommand())
            {
                updCash.Transaction = tx;
                updCash.CommandText = @"
UPDATE CashOnHand
   SET Amount = Amount - @a,
       UpdatedAt = CURRENT_TIMESTAMP
 WHERE UserId=@u;";
                updCash.Parameters.AddWithValue("@a", amount);
                updCash.Parameters.AddWithValue("@u", userId);
                updCash.ExecuteNonQuery();
            }

            // 3) Dodaj do salda rachunku (konto musi byæ u¿ytkownika)
            using (var updAcc = c.CreateCommand())
            {
                updAcc.Transaction = tx;
                updAcc.CommandText = @"
UPDATE BankAccounts
   SET Balance = Balance + @a
 WHERE Id=@id AND UserId=@u;";
                updAcc.Parameters.AddWithValue("@a", amount);
                updAcc.Parameters.AddWithValue("@id", accountId);
                updAcc.Parameters.AddWithValue("@u", userId);
                var rows = updAcc.ExecuteNonQuery();
                if (rows == 0) throw new InvalidOperationException("Nie znaleziono rachunku lub nie nale¿y do u¿ytkownika.");
            }

            tx.Commit();
        }


        public sealed class CategoryAmountDto
        {
            public string Name { get; set; } = "";
            public decimal Amount { get; set; }
        }

        /// <summary>
        /// WYDATKI wg kategorii (bie¿¹cy zakres) – bezpieczna metoda.
        /// Stara siê dopasowaæ do 2 typowych schematów:
        ///  A) tabela Transactions(Type='expense', CategoryId, Amount, Date, UserId) + Categories(Id, Name)
        ///  B) tabela Expenses(CategoryId, Amount, Date, UserId) + Categories(Id, Name)
        /// Kwoty ujemne s¹ brane bezwzglêdnie.
        /// </summary>
        private static bool TableExists(SqliteConnection con, string name)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", name);
            return cmd.ExecuteScalar() != null;
        }

        public static List<CategoryAmountDto> GetSpendingByCategorySafe(int userId, DateTime start, DateTime end)
        {
            using var con = GetConnection();

            bool hasTrans = TableExists(con, "Transactions");
            bool hasExpenses = TableExists(con, "Expenses");
            bool hasCats = TableExists(con, "Categories"); // nie wymagane, ale ³adniejsze nazwy

            if (!hasTrans && !hasExpenses)
                return new List<CategoryAmountDto>();

            var sb = new StringBuilder();
            void AppendUnionIfNeeded()
            {
                if (sb.Length > 0) sb.AppendLine("UNION ALL");
            }

            if (hasTrans)
            {
                AppendUnionIfNeeded();
                sb.AppendLine(@"
SELECT COALESCE(" + (hasCats ? "c.Name" : "NULL") + @", '(brak kategorii)') AS Name,
       SUM(ABS(t.Amount)) AS Amount
FROM Transactions t
" + (hasCats ? "LEFT JOIN Categories c ON c.Id = t.CategoryId" : "") + @"
WHERE t.UserId = $uid
  AND date(t.Date) BETWEEN date($start) AND date($end)
  AND (LOWER(t.Type) = 'expense' OR t.Type = 0)
GROUP BY COALESCE(" + (hasCats ? "c.Name" : "NULL") + @", '(brak kategorii)') 
HAVING SUM(ABS(t.Amount)) > 0
");
            }

            if (hasExpenses)
            {
                AppendUnionIfNeeded();
                sb.AppendLine(@"
SELECT COALESCE(" + (hasCats ? "c.Name" : "NULL") + @", '(brak kategorii)') AS Name,
       SUM(ABS(e.Amount)) AS Amount
FROM Expenses e
" + (hasCats ? "LEFT JOIN Categories c ON c.Id = e.CategoryId" : "") + @"
WHERE e.UserId = $uid
  AND date(e.Date) BETWEEN date($start) AND date($end)
GROUP BY COALESCE(" + (hasCats ? "c.Name" : "NULL") + @", '(brak kategorii)') 
HAVING SUM(ABS(e.Amount)) > 0
");
            }

            using var cmd = con.CreateCommand();
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.IsDBNull(0) ? "(brak kategorii)" : r.GetString(0);
                var amt = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
                if (amt <= 0) continue;
                if (dict.ContainsKey(name)) dict[name] += amt; else dict[name] = amt;
            }

            return dict.Select(kv => new CategoryAmountDto { Name = kv.Key, Amount = kv.Value })
                       .OrderByDescending(x => x.Amount)
                       .ToList();
        }

        public static List<CategoryAmountDto> GetIncomeBySourceSafe(int userId, DateTime start, DateTime end)
        {
            using var con = GetConnection();

            bool hasTrans = TableExists(con, "Transactions");
            bool hasIncomes = TableExists(con, "Incomes");
            bool hasCats = TableExists(con, "Categories");

            if (!hasTrans && !hasIncomes)
                return new List<CategoryAmountDto>();

            var sb = new StringBuilder();
            void AppendUnionIfNeeded()
            {
                if (sb.Length > 0) sb.AppendLine("UNION ALL");
            }

            if (hasTrans)
            {
                AppendUnionIfNeeded();
                sb.AppendLine(@"
SELECT COALESCE(t.Source, " + (hasCats ? "COALESCE(c.Name,'Przychody')" : "'Przychody'") + @") AS Name,
       SUM(ABS(t.Amount)) AS Amount
FROM Transactions t
" + (hasCats ? "LEFT JOIN Categories c ON c.Id = t.CategoryId" : "") + @"
WHERE t.UserId = $uid
  AND date(t.Date) BETWEEN date($start) AND date($end)
  AND (LOWER(t.Type) = 'income' OR t.Type = 1)
GROUP BY COALESCE(t.Source, " + (hasCats ? "COALESCE(c.Name,'Przychody')" : "'Przychody'") + @")
HAVING SUM(ABS(t.Amount)) > 0
");
            }

            if (hasIncomes)
            {
                AppendUnionIfNeeded();
                sb.AppendLine(@"
SELECT COALESCE(i.Source,'Przychody') AS Name,
       SUM(ABS(i.Amount)) AS Amount
FROM Incomes i
WHERE i.UserId = $uid
  AND date(i.Date) BETWEEN date($start) AND date($end)
GROUP BY COALESCE(i.Source,'Przychody')
HAVING SUM(ABS(i.Amount)) > 0
");
            }

            using var cmd = con.CreateCommand();
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.IsDBNull(0) ? "Przychody" : r.GetString(0);
                var amt = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
                if (amt <= 0) continue;
                if (dict.ContainsKey(name)) dict[name] += amt; else dict[name] = amt;
            }

            return dict.Select(kv => new CategoryAmountDto { Name = kv.Key, Amount = kv.Value })
                       .OrderByDescending(x => x.Amount)
                       .ToList();
        }

    }
}











