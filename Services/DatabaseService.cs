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
    /// Centralny serwis SQLite – spójny z wywo³aniami w UI.
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

        public static void EnsureTables()
        {
            lock (_schemaLock)
            {
                if (_schemaInitialized) return;
                using var c = GetConnection();
                SchemaService.Ensure(c);
                EnsureBankAccountsSchema(c);
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
                        EnsureBankAccountsSchema(c0);   // MIGRACJA BankName w BankAccounts
                        _schemaInitialized = true;
                    }
                }
            }
            return GetConnection();
        }

        // Add a simple event to notify UI about data changes so pages can refresh
        public static event EventHandler? DataChanged;

        private static void RaiseDataChanged()
        {
            try
            {
                DataChanged?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
                // ignore exceptions from handlers
            }
        }

        // ===== PRZENOSZENIE GOTÓWKI MIÊDZY PULAMI =====
        /// <summary>
        /// Przeniesienie z wolnej gotówki do od³o¿onej.
        /// Wolna gotówka = CashOnHand - SavedCash.
        /// Saved roœnie, free maleje, suma (free + saved) zostaje taka sama.
        /// Dodatkowo korygujemy CashOnHand tak, ¿eby zawsze zgadza³o siê: CashOnHand = free + saved.
        /// </summary>
        public static void TransferFreeToSaved(int userId, decimal amount)
        {
            if (amount <= 0) return;

            var allCash = GetCashOnHand(userId);
            var saved = GetSavedCash(userId);
            var free = Math.Max(0m, allCash - saved);

            if (free < amount)
                throw new InvalidOperationException("Za ma³o wolnej gotówki.");

            var newSaved = saved + amount;
            var newFree = free - amount;
            var newAll = newSaved + newFree;

            SetSavedCash(userId, newSaved);
            SetCashOnHand(userId, newAll);

            RaiseDataChanged();
        }

        /// <summary>
        /// Przeniesienie z od³o¿onej gotówki do wolnej.
        /// Wolna gotówka = CashOnHand - SavedCash.
        /// Saved maleje, free roœnie, suma (free + saved) zostaje taka sama.
        /// Dodatkowo korygujemy CashOnHand tak, ¿eby zawsze zgadza³o siê: CashOnHand = free + saved.
        /// </summary>
        public static void TransferSavedToFree(int userId, decimal amount)
        {
            if (amount <= 0) return;

            var allCash = GetCashOnHand(userId);
            var saved = GetSavedCash(userId);
            var free = Math.Max(0m, allCash - saved);

            if (saved < amount)
                throw new InvalidOperationException("Za ma³o od³o¿onej gotówki.");

            var newSaved = saved - amount;
            var newFree = free + amount;
            var newAll = newSaved + newFree;

            SetSavedCash(userId, newSaved);
            SetCashOnHand(userId, newAll);

            RaiseDataChanged();
        }

        /// <summary>
        /// Przeniesienie œrodków z od³o¿onej gotówki na konto bankowe.
        /// Zmniejsza SavedCash i CashOnHand, zwiêksza wybrane konto.
        /// </summary>
        public static void TransferSavedToBank(int userId, int accountId, decimal amount)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));

            var saved = GetSavedCash(userId);
            if (saved < amount)
                throw new InvalidOperationException("Za ma³o od³o¿onej gotówki.");

            var cash = GetCashOnHand(userId);
            if (cash < amount)
                throw new InvalidOperationException("Za ma³o gotówki w portfelu.");

            SetSavedCash(userId, saved - amount);
            SetCashOnHand(userId, cash - amount);

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE BankAccounts
   SET Balance = Balance + @a
 WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@id", accountId);
            cmd.Parameters.AddWithValue("@u", userId);
            var rows = cmd.ExecuteNonQuery();
            if (rows == 0)
                throw new InvalidOperationException("Nie znaleziono rachunku bankowego lub nie nale¿y do u¿ytkownika.");

            RaiseDataChanged();
        }

        /// <summary>
        /// Przeniesienie œrodków z konta bankowego do od³o¿onej gotówki.
        /// </summary>
        public static void TransferBankToSaved(int userId, int accountId, decimal amount)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

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

            using (var up = c.CreateCommand())
            {
                up.Transaction = tx;
                up.CommandText = @"UPDATE BankAccounts SET Balance = Balance - @a WHERE Id=@id AND UserId=@u;";
                up.Parameters.AddWithValue("@a", amount);
                up.Parameters.AddWithValue("@id", accountId);
                up.Parameters.AddWithValue("@u", userId);
                up.ExecuteNonQuery();
            }

            tx.Commit();

            var cash = GetCashOnHand(userId);
            SetCashOnHand(userId, cash + amount);
            AddToSavedCash(userId, amount);
        }

        /// <summary>
        /// Przeniesienie œrodków miêdzy kopertami.
        /// </summary>
        public static void TransferEnvelopeToEnvelope(int userId, int fromEnvelopeId, int toEnvelopeId, decimal amount)
        {
            if (amount <= 0 || fromEnvelopeId == toEnvelopeId) return;

            SubtractFromEnvelopeAllocated(userId, fromEnvelopeId, amount);
            AddToEnvelopeAllocated(userId, toEnvelopeId, amount);
        }

        /// <summary>
        /// Od³o¿ona gotówka (poza kopertami) -> koperta.
        /// SavedCash siê nie zmienia, zmienia siê tylko podzia³.
        /// </summary>
        public static void TransferSavedToEnvelope(int userId, int envelopeId, decimal amount)
        {
            if (amount <= 0) return;

            var saved = GetSavedCash(userId);
            var allocated = GetTotalAllocatedInEnvelopesForUser(userId);
            var unassigned = saved - allocated;

            if (unassigned < amount)
                throw new InvalidOperationException("Za ma³o od³o¿onej gotówki poza kopertami.");

            AddToEnvelopeAllocated(userId, envelopeId, amount);
        }

        /// <summary>
        /// Koperta -> od³o¿ona gotówka (poza kopertami).
        /// </summary>
        public static void TransferEnvelopeToSaved(int userId, int envelopeId, decimal amount)
        {
            if (amount <= 0) return;

            SubtractFromEnvelopeAllocated(userId, envelopeId, amount);
            // SavedCash siê nie zmienia – dalej jest od³o¿ona, tylko bez przypisania do koperty
        }

        /// <summary>
        /// Wolna gotówka -> koperta.
        /// Zwiêksza SavedCash oraz przydzia³ w kopercie, fizyczna gotówka bez zmian.
        /// </summary>
        public static void TransferFreeToEnvelope(int userId, int envelopeId, decimal amount)
        {
            if (amount <= 0) return;

            var allCash = GetCashOnHand(userId);
            var saved = GetSavedCash(userId);
            var free = Math.Max(0m, allCash - saved);

            if (free < amount)
                throw new InvalidOperationException("Za ma³o wolnej gotówki.");

            SetSavedCash(userId, saved + amount);
            AddToEnvelopeAllocated(userId, envelopeId, amount);
        }

        /// <summary>
        /// Koperta -> wolna gotówka.
        /// Zmniejsza SavedCash i przydzia³ w kopercie.
        /// </summary>
        public static void TransferEnvelopeToFree(int userId, int envelopeId, decimal amount)
        {
            if (amount <= 0) return;

            var saved = GetSavedCash(userId);
            if (saved < amount)
                throw new InvalidOperationException("Za ma³o od³o¿onej gotówki.");

            SubtractFromEnvelopeAllocated(userId, envelopeId, amount);
            SetSavedCash(userId, saved - amount);
        }

        /// <summary>
        /// Przelew miêdzy dwoma kontami bankowymi tego samego u¿ytkownika.
        /// </summary>
        public static void TransferBankToBank(int userId, int fromAccountId, int toAccountId, decimal amount)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));
            if (fromAccountId == toAccountId) return;

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

            decimal fromBal;
            using (var chk = c.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = @"SELECT Balance FROM BankAccounts WHERE Id=@id AND UserId=@u LIMIT 1;";
                chk.Parameters.AddWithValue("@id", fromAccountId);
                chk.Parameters.AddWithValue("@u", userId);
                var obj = chk.ExecuteScalar();
                if (obj == null || obj == DBNull.Value)
                    throw new InvalidOperationException("Nie znaleziono rachunku Ÿród³owego lub nie nale¿y do u¿ytkownika.");
                fromBal = Convert.ToDecimal(obj);
            }

            if (fromBal < amount)
                throw new InvalidOperationException("Na rachunku Ÿród³owym brakuje œrodków na taki przelew.");

            using (var updFrom = c.CreateCommand())
            {
                updFrom.Transaction = tx;
                updFrom.CommandText = @"UPDATE BankAccounts SET Balance = Balance - @a WHERE Id=@id AND UserId=@u;";
                updFrom.Parameters.AddWithValue("@a", amount);
                updFrom.Parameters.AddWithValue("@id", fromAccountId);
                updFrom.Parameters.AddWithValue("@u", userId);
                updFrom.ExecuteNonQuery();
            }

            using (var updTo = c.CreateCommand())
            {
                updTo.Transaction = tx;
                updTo.CommandText = @"UPDATE BankAccounts SET Balance = Balance + @a WHERE Id=@id AND UserId=@u;";
                updTo.Parameters.AddWithValue("@a", amount);
                updTo.Parameters.AddWithValue("@id", toAccountId);
                updTo.Parameters.AddWithValue("@u", userId);
                var rowsTo = updTo.ExecuteNonQuery();
                if (rowsTo == 0)
                    throw new InvalidOperationException("Nie znaleziono rachunku docelowego lub nie nale¿y do u¿ytkownika.");
            }

            tx.Commit();
        }

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

            // ===== Tabele z danymi u¿ytkownika =====
            if (Has("Incomes"))
                Exec("DELETE FROM Incomes WHERE UserId = @uid;");

            if (Has("Expenses"))
                Exec("DELETE FROM Expenses WHERE UserId = @uid;");

            if (Has("Envelopes"))
                Exec("DELETE FROM Envelopes WHERE UserId = @uid;");

            if (Has("CashOnHand"))
                Exec("DELETE FROM CashOnHand WHERE UserId = @uid;");

            if (Has("SavedCash"))
                Exec("DELETE FROM SavedCash WHERE UserId = @uid;");

            if (Has("BankAccounts"))
                Exec("DELETE FROM BankAccounts WHERE UserId = @uid;");

            if (Has("BankConnections"))
                Exec("DELETE FROM BankConnections WHERE UserId = @uid;");

            if (Has("Categories"))
                Exec("DELETE FROM Categories WHERE UserId = @uid;");

            // ===== Na koñcu sam u¿ytkownik =====
            if (Has("Users"))
                Exec("DELETE FROM Users WHERE Id = @uid;");

            tx.Commit();
        }

        //=== strona po pierwszym logowaniu na nowe konto===
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
        }

        /// <summary>
        /// Alias dla zgodnoœci – wiele miejsc wo³a GetEnvelopeNames, a w³aœciwa
        /// implementacja jest w GetEnvelopesNames.
        /// </summary>
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
            catch
            {
                // jeœli coœ pójdzie nie tak, po prostu zwracamy pust¹ listê
            }

            return result;
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
        // ======================= KREDYTY =======================
        // =========================================================
        // Dodaj poni¿sze metody wewn¹trz klasy `DatabaseService` (namespace Finly.Services).

        public static System.Collections.Generic.List<Finly.Models.LoanModel> GetLoans(int userId)
        {
            var list = new System.Collections.Generic.List<Finly.Models.LoanModel>();
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
                list.Add(new Finly.Models.LoanModel
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

        public static int InsertLoan(Finly.Models.LoanModel loan)
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

            var idObj = cmd.ExecuteScalar();
            var id = Convert.ToInt32(idObj);
            RaiseDataChanged();
            return id;
        }

        public static void UpdateLoan(Finly.Models.LoanModel loan)
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
        // ======================= KATEGORIE =======================
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

        // ===== WYDATKI – operacje na Ÿród³ach pieniêdzy =====

        public static void SpendFromFreeCash(int userId, decimal amount)
        {
            if (amount <= 0) return;

            var allCash = GetCashOnHand(userId);
            var saved = GetSavedCash(userId);
            var free = Math.Max(0m, allCash - saved);

            if (free < amount)
                throw new InvalidOperationException("Za ma³o wolnej gotówki na taki wydatek.");

            SetCashOnHand(userId, allCash - amount);
        }

        public static void SpendFromSavedCash(int userId, decimal amount)
        {
            if (amount <= 0) return;

            SubtractFromSavedCash(userId, amount);

            var allCash = GetCashOnHand(userId);
            if (allCash < amount)
                throw new InvalidOperationException("Za ma³o gotówki na taki wydatek.");

            SetCashOnHand(userId, allCash - amount);
        }

        public static void SpendFromEnvelope(int userId, int envelopeId, decimal amount)
        {
            if (amount <= 0) return;

            SubtractFromEnvelopeAllocated(userId, envelopeId, amount);
            SubtractFromSavedCash(userId, amount);

            var allCash = GetCashOnHand(userId);
            if (allCash < amount)
                throw new InvalidOperationException("Za ma³o gotówki na taki wydatek.");

            SetCashOnHand(userId, allCash - amount);
        }

        public static void SpendFromBankAccount(int userId, int accountId, decimal amount)
        {
            if (amount <= 0) return;

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

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
                throw new InvalidOperationException("Na koncie bankowym brakuje œrodków na taki wydatek.");

            using (var upd = c.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = @"UPDATE BankAccounts SET Balance = Balance - @a WHERE Id=@id AND UserId=@u;";
                upd.Parameters.AddWithValue("@a", amount);
                upd.Parameters.AddWithValue("@id", accountId);
                upd.Parameters.AddWithValue("@u", userId);
                upd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public sealed class CategoryTransactionDto
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Description { get; set; } = "";
        }

        public static DataTable GetCategories(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            if (ColumnExists(c, "Categories", "IsArchived"))
            {
                cmd.CommandText = "SELECT Id, Name FROM Categories WHERE UserId=@u AND IsArchived = 0 ORDER BY Name;";
            }
            else
            {
                cmd.CommandText = "SELECT Id, Name FROM Categories WHERE UserId=@u ORDER BY Name;";
            }

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
            {
                cmd.CommandText = "SELECT Name FROM Categories WHERE UserId=@u AND IsArchived = 0 ORDER BY Name;";
            }
            else
            {
                cmd.CommandText = "SELECT Name FROM Categories WHERE UserId=@u ORDER BY Name;";
            }

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
            {
                sql += " AND IsArchived = 0";
            }

            sql += " LIMIT 1;";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name.Trim());
            var obj = cmd.ExecuteScalar();
            return (obj == null || obj == DBNull.Value) ? (int?)null : Convert.ToInt32(obj);
        }

        /// <summary>
        /// Tworzy now¹ kategoriê. Mo¿esz w przysz³oœci rozszerzyæ o typ, kolor, ikonê.
        /// </summary>
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

        /// <summary>
        /// Historyczne usuwanie kategorii – fizycznie z tabeli.
        /// Przy nowym podejœciu lepiej u¿ywaæ ArchiveCategory (miêkkie usuniêcie).
        /// </summary>
        public static void DeleteCategory(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Categories WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Miêkka archiwizacja kategorii (IsArchived = 1). Jeœli kolumna nie istnieje – fallback do DELETE.
        /// </summary>
        public static void ArchiveCategory(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            if (!ColumnExists(c, "Categories", "IsArchived"))
            {
                cmd.CommandText = "DELETE FROM Categories WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return;
            }

            cmd.CommandText = "UPDATE Categories SET IsArchived = 1 WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Podsumowanie kategorii w zadanym okresie – liczba transakcji, suma, udzia³ %.
        /// U¿ywane przez stronê Kategorie / Dashboard.
        /// </summary>
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
            {
                sb.Append("  AND c.IsArchived = 0");
            }

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
ORDER BY TotalAmount DESC;
");

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
                var total = r.IsDBNull(4) ? 0m : r.GetDecimal(4);

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

        /// <summary>
        /// Ostatnie transakcje w danej kategorii – do panelu szczegó³ów kategorii.
        /// </summary>
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
                decimal amount;
                var val = r.GetValue(1);
                if (val is decimal dec) amount = dec;
                else if (val is double d) amount = (decimal)d;
                else amount = Convert.ToDecimal(val);

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

        /// <summary>
        /// Scala dwie kategorie: przenosi transakcje ze Ÿród³owej na docelow¹ i archiwizuje/usuwa Ÿród³ow¹.
        /// </summary>
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
                {
                    arch.CommandText = @"UPDATE Categories SET IsArchived = 1 WHERE Id=@src AND UserId=@u;";
                }
                else
                {
                    arch.CommandText = @"DELETE FROM Categories WHERE Id=@src AND UserId=@u;";
                }

                arch.Parameters.AddWithValue("@src", sourceCategoryId);
                arch.Parameters.AddWithValue("@u", userId);
                arch.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // =========================================================
        // ========================= KONTA =========================
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
            return Convert.ToInt32(cmd.ExecuteScalar());
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

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // FK failed
            {
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
SELECT 
    e.Id,
    e.UserId,
    e.Date,
    e.Amount,
    e.Description,
    e.CategoryId,
    COALESCE(c.Name,'(brak)') AS CategoryName,
    e.AccountId
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

            if (accountId != null)
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
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT Id, UserId, Amount, Date, Description, CategoryId
FROM Expenses 
WHERE Id=@id 
LIMIT 1;";
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
    UserId=@u, 
    Amount=@a, 
    Date=@d, 
    Description=@desc, 
    CategoryId=@c
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

        /// <summary>
        /// Historyczny alias – teraz po prostu wo³a InsertExpense i ignoruje zwracane Id.
        /// </summary>
        public static void AddExpense(Expense e)
        {
            _ = InsertExpense(e);
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
        // ======================== KOPERTY ========================
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

        /// <summary>
        /// Zwraca listê celów na podstawie kopert:
        /// bierzemy tylko te, które maj¹ dodatni¹ docelow¹ kwotê (Target > 0).
        /// Jeœli w tabeli s¹ kolumny Deadline / GoalText – korzystamy z nich.
        /// </summary>
        public static List<EnvelopeGoalDto> GetEnvelopeGoals(int userId)
        {
            var list = new List<EnvelopeGoalDto>();

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();

            bool hasDeadline = ColumnExists(c, "Envelopes", "Deadline");
            bool hasGoalText = ColumnExists(c, "Envelopes", "GoalText");

            // ===== budowa SQL =====
            var sql = @"
SELECT 
    Id,
    Name,
    Target,
    COALESCE(Allocated,0) AS Allocated";

            // kolumna z deadlinem (jeœli nie ma – zwracamy NULL)
            sql += hasDeadline ? ", Deadline" : ", NULL AS Deadline";

            // tekst celu: osobna kolumna GoalText lub fallback na Note
            sql += hasGoalText ? ", GoalText" : ", Note AS GoalText";

            sql += @"
FROM Envelopes
WHERE UserId = @u
  AND Target IS NOT NULL
  AND Target > 0";

            // jeœli mamy kolumnê Deadline, traktujemy j¹ jako warunek:
            // cel jest tylko wtedy, gdy Deadline nie jest NULL
            if (hasDeadline)
            {
                sql += " AND Deadline IS NOT NULL";
            }

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

                // 1) próba odczytu Deadline z kolumny (jeœli istnieje)
                if (hasDeadline)
                {
                    var dt = GetDate(r, 4);
                    dto.Deadline = dt == DateTime.MinValue ? (DateTime?)null : dt;
                }

                // 2) jeœli Deadline jest puste, spróbujmy wyci¹gn¹æ je z tekstu celu / notatki
                if (dto.Deadline == null && !string.IsNullOrWhiteSpace(dto.GoalText))
                {
                    dto.Deadline = TryParseDeadlineFromGoalText(dto.GoalText);
                }

                list.Add(dto);
            }

            return list;
        }


        // === POMOCNICZE: parsowanie terminu z tekstu notatki / celu ===
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

                    // najpierw format "yyyy-MM-dd"
                    if (DateTime.TryParseExact(
                            dateText,
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var d1))
                    {
                        return d1.Date;
                    }

                    // potem lokalny format (np. 12.10.2026)
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

        /// <summary>
        /// Czyœci dane celu w kopercie, ale NIE usuwa samej koperty
        /// i NIE zmienia Target / Allocated (¿eby nie waliæ w NOT NULL).
        /// Dziêki temu koperta zostaje, a znikaj¹ tylko dane celu.
        /// </summary>
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

            // jeœli mamy osobn¹ kolumnê GoalText – czyœcimy j¹,
            // w przeciwnym razie czyœcimy Note (tam trzymamy opis celu)
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
        }


        /// <summary>
        /// Ustawia / aktualizuje dane celu dla danej koperty:
        /// Target, Allocated, Deadline oraz opis (GoalText lub Note).
        /// </summary>
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

            // opis celu: jeœli jest kolumna GoalText – u¿ywamy jej,
            // w przeciwnym razie zapisujemy do Note
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
            var id = Convert.ToInt32(idObj);

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

        // GOTÓWKA
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
            up.CommandText = @"
INSERT INTO CashOnHand(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE 
SET Amount=excluded.Amount, UpdatedAt=CURRENT_TIMESTAMP;";
            up.Parameters.AddWithValue("@u", userId);
            up.Parameters.AddWithValue("@a", amount);
            up.ExecuteNonQuery();

            RaiseDataChanged();
        }

        // ===== GOTÓWKA OD£O¯ONA (SavedCash) =====

        public static decimal GetSavedCash(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Amount FROM SavedCash WHERE UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            var obj = cmd.ExecuteScalar();
            return (obj == null || obj == DBNull.Value) ? 0m : Convert.ToDecimal(obj);
        }

        public static void SetSavedCash(int userId, decimal amount)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO SavedCash(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE 
SET Amount = excluded.Amount,
    UpdatedAt = CURRENT_TIMESTAMP;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();

            RaiseDataChanged();
        }

        public static void AddToSavedCash(int userId, decimal amount)
        {
            if (amount <= 0) return;
            var current = GetSavedCash(userId);
            SetSavedCash(userId, current + amount);

            // SetSavedCash already raises DataChanged
        }

        public static void SubtractFromSavedCash(int userId, decimal amount)
        {
            if (amount <= 0) return;

            var current = GetSavedCash(userId);
            if (current < amount)
                throw new InvalidOperationException("Za ma³o œrodków w gotówce od³o¿onej.");

            SetSavedCash(userId, current - amount);

            // SetSavedCash raises DataChanged
        }

        public static void AddToEnvelopeAllocated(int userId, int envelopeId, decimal amount)
        {
            if (amount <= 0) return;

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE Envelopes
   SET Allocated = COALESCE(Allocated,0) + @a
 WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@id", envelopeId);
            cmd.Parameters.AddWithValue("@u", userId);

            var rows = cmd.ExecuteNonQuery();
            if (rows == 0)
                throw new InvalidOperationException("Nie znaleziono wybranej koperty.");

            RaiseDataChanged();
        }

        public static void SubtractFromEnvelopeAllocated(int userId, int envelopeId, decimal amount)
        {
            if (amount <= 0) return;

            using var c = OpenAndEnsureSchema();
            decimal current;

            using (var q = c.CreateCommand())
            {
                q.CommandText = @"
SELECT COALESCE(Allocated,0) 
FROM Envelopes 
WHERE Id=@id AND UserId=@u 
LIMIT 1;";
                q.Parameters.AddWithValue("@id", envelopeId);
                q.Parameters.AddWithValue("@u", userId);
                current = Convert.ToDecimal(q.ExecuteScalar() ?? 0m);
            }

            if (current < amount)
                throw new InvalidOperationException("W kopercie jest za ma³o œrodków.");

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE Envelopes
   SET Allocated = COALESCE(Allocated,0) - @a
 WHERE Id=@id AND UserId=@u;";
                cmd.Parameters.AddWithValue("@a", amount);
                cmd.Parameters.AddWithValue("@id", envelopeId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.ExecuteNonQuery();
            }

            RaiseDataChanged();
        }

        // =========================================================
        // ======================= PODSUMOWANIA ====================
        // =========================================================

        public static decimal GetTotalAllocatedInEnvelopesForUser(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Allocated),0) FROM Envelopes WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        public sealed class MoneySnapshot
        {
            public decimal Banks { get; set; }      // konta bankowe
            public decimal Cash { get; set; }       // wolna gotówka
            public decimal Saved { get; set; }      // od³o¿ona gotówka (ca³a pula)
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

        private static decimal GetTotalBanksBalance(int userId)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Balance),0) FROM BankAccounts WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        // == PRZELEWY ==

        public static void TransferBankToCash(int userId, int accountId, decimal amount)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

            decimal currentBal;
            using (var chk = c.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = @"
SELECT Balance 
FROM BankAccounts 
WHERE Id=@id AND UserId=@u 
LIMIT 1;";
                chk.Parameters.AddWithValue("@id", accountId);
                chk.Parameters.AddWithValue("@u", userId);
                var obj = chk.ExecuteScalar();
                if (obj == null || obj == DBNull.Value)
                    throw new InvalidOperationException("Nie znaleziono rachunku lub nie nale¿y do u¿ytkownika.");
                currentBal = Convert.ToDecimal(obj);
            }
            if (currentBal < amount)
                throw new InvalidOperationException("Na rachunku brakuje œrodków na tak¹ wyp³atê.");

            using (var up = c.CreateCommand())
            {
                up.Transaction = tx;
                up.CommandText = @"
UPDATE BankAccounts 
SET Balance = Balance - @a 
WHERE Id=@id AND UserId=@u;";
                up.Parameters.AddWithValue("@a", amount);
                up.Parameters.AddWithValue("@id", accountId);
                up.Parameters.AddWithValue("@u", userId);
                up.ExecuteNonQuery();
            }

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

        public static void TransferCashToBank(int userId, int accountId, decimal amount)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi byæ dodatnia.", nameof(amount));

            using var c = OpenAndEnsureSchema();
            using var tx = c.BeginTransaction();

            decimal currentCash;
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = @"
SELECT COALESCE(Amount,0) 
FROM CashOnHand 
WHERE UserId=@u 
LIMIT 1;";
                q.Parameters.AddWithValue("@u", userId);
                currentCash = Convert.ToDecimal(q.ExecuteScalar() ?? 0m);
            }
            if (currentCash < amount)
                throw new InvalidOperationException("Za ma³o gotówki na tak¹ wp³atê.");

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
                if (rows == 0)
                    throw new InvalidOperationException("Nie znaleziono rachunku lub nie nale¿y do u¿ytkownika.");
            }

            tx.Commit();
        }

        /// <summary>
        /// Przeci¹¿enia pod now¹ stronê „Dodaj” – wersja z kategori¹, dat¹ i opisem.
        /// Na razie kategoria/opis nie s¹ zapisywane w tabeli transferów – to tylko
        /// rozszerzona sygnatura zgodna z UI.
        /// </summary>
        public static void TransferBankToCash(
            int userId,
            int accountId,
            decimal amount,
            int? categoryId,
            DateTime date,
            string? description)
        {
            TransferBankToCash(userId, accountId, amount);
        }

        public static void TransferCashToBank(
            int userId,
            int accountId,
            decimal amount,
            int? categoryId,
            DateTime date,
            string? description)
        {
            TransferCashToBank(userId, accountId, amount);
        }

        // =========================================================
        // ====================== ZESTAWIENIA ======================
        // =========================================================

        private static bool TableExists(SqliteConnection con, string name)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", name);
            return cmd.ExecuteScalar() != null;
        }

        private static bool ColumnExists(SqliteConnection con, string table, string column)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// MIGRACJA: dopilnuj, ¿eby w BankAccounts by³a kolumna BankName (TEXT).
        /// </summary>
        private static void EnsureBankAccountsSchema(SqliteConnection con)
        {
            if (!TableExists(con, "BankAccounts")) return;

            if (!ColumnExists(con, "BankAccounts", "BankName"))
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = "ALTER TABLE BankAccounts ADD COLUMN BankName TEXT;";
                cmd.ExecuteNonQuery();
            }
        }

        public sealed class CategoryAmountDto
        {
            public string Name { get; set; } = "";
            public decimal Amount { get; set; }
        }

        public static List<CategoryAmountDto> GetSpendingByCategorySafe(int userId, DateTime start, DateTime end)
        {
            using var con = OpenAndEnsureSchema();

            bool hasTrans = TableExists(con, "Transactions");
            bool hasExpenses = TableExists(con, "Expenses");
            bool hasCats = TableExists(con, "Categories");

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
            using var con = OpenAndEnsureSchema();

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

        /// <summary>
        /// Najwy¿sze wydatki wg „sklepu/kontrahenta” – SAFE:
        /// Transactions: u¿ywa kolumn (w kolejnoœci priorytetu): Merchant, Payee, Title, Note.
        /// Expenses: u¿ywa Description (albo Title, jeœli Description brak).
        /// Uwzglêdnia tylko wydatki i sumuje wartoœci bezwzglêdne.
        /// </summary>
        public static List<CategoryAmountDto> GetSpendingByMerchantSafe(int userId, DateTime start, DateTime end)
        {
            using var con = OpenAndEnsureSchema();

            bool hasTrans = TableExists(con, "Transactions");
            bool hasExpenses = TableExists(con, "Expenses");

            if (!hasTrans && !hasExpenses)
                return new List<CategoryAmountDto>();

            string TxCol(SqliteConnection c)
            {
                if (ColumnExists(c, "Transactions", "Merchant")) return "t.Merchant";
                if (ColumnExists(c, "Transactions", "Payee")) return "t.Payee";
                if (ColumnExists(c, "Transactions", "Title")) return "t.Title";
                if (ColumnExists(c, "Transactions", "Note")) return "t.Note";
                return "NULL";
            }

            string ExCol(SqliteConnection c)
            {
                if (ColumnExists(c, "Expenses", "Description")) return "e.Description";
                if (ColumnExists(c, "Expenses", "Title")) return "e.Title";
                return "NULL";
            }

            string txNameExpr = $"COALESCE(NULLIF({TxCol(con)},''), '(brak sklepu)')";
            string exNameExpr = $"COALESCE(NULLIF({ExCol(con)},''), '(brak sklepu)')";

            var sb = new StringBuilder();
            void AppendUnionIfNeeded()
            {
                if (sb.Length > 0) sb.AppendLine("UNION ALL");
            }

            if (hasTrans)
            {
                AppendUnionIfNeeded();
                sb.AppendLine($@"
SELECT {txNameExpr} AS Name,
       SUM(ABS(t.Amount)) AS Amount
FROM Transactions t
WHERE t.UserId = $uid
  AND date(t.Date) BETWEEN date($start) AND date($end)
  AND (LOWER(t.Type) = 'expense' OR t.Type = 0)
GROUP BY {txNameExpr}
HAVING SUM(ABS(t.Amount)) > 0
");
            }

            if (hasExpenses)
            {
                AppendUnionIfNeeded();
                sb.AppendLine($@"
SELECT {exNameExpr} AS Name,
       SUM(ABS(e.Amount)) AS Amount
FROM Expenses e
WHERE e.UserId = $uid
  AND date(e.Date) BETWEEN date($start) AND date($end)
GROUP BY {exNameExpr}
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
                var name = r.IsDBNull(0) ? "(brak sklepu)" : r.GetString(0);
                var amt = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
                if (amt <= 0) continue;
                if (dict.ContainsKey(name)) dict[name] += amt; else dict[name] = amt;
            }

            return dict.Select(kv => new CategoryAmountDto { Name = kv.Key, Amount = kv.Value })
                       .OrderByDescending(x => x.Amount)
                       .ToList();
        }

        // =========================================================
        // ======================== PRZYCHODY ======================
        // =========================================================

        public sealed class Income
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public double Amount { get; set; }
            public DateTime Date { get; set; }
            public string? Description { get; set; }
            public string? Source { get; set; }   // opcjonalne pole
        }

        public static int InsertIncome(Income i)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Incomes(UserId, Amount, Date, Description, Source)
VALUES (@u,@a,@d,@desc,@s);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", i.UserId);
            cmd.Parameters.AddWithValue("@a", i.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(i.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)i.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@s", (object?)i.Source ?? DBNull.Value);
            return Convert.ToInt32((long)(cmd.ExecuteScalar() ?? 0L));
        }

        public static void UpdateIncome(Income i)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE Incomes SET
  UserId=@u, Amount=@a, Date=@d, Description=@desc, Source=@s
WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", i.Id);
            cmd.Parameters.AddWithValue("@u", i.UserId);
            cmd.Parameters.AddWithValue("@a", i.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(i.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)i.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@s", (object?)i.Source ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public static void DeleteIncome(int id)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Incomes WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static DataTable GetIncomes(
            int userId, DateTime? from = null, DateTime? to = null, string? search = null)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            var sb = new StringBuilder(@"
SELECT Id, UserId, Amount, Date, Description, Source
FROM Incomes
WHERE UserId=@u");
            cmd.Parameters.AddWithValue("@u", userId);

            if (from != null)
            {
                sb.Append(" AND date(Date) >= date(@f)");
                cmd.Parameters.AddWithValue("@f", from.Value.ToString("yyyy-MM-dd"));
            }
            if (to != null)
            {
                sb.Append(" AND date(Date) <= date(@t)");
                cmd.Parameters.AddWithValue("@t", to.Value.ToString("yyyy-MM-dd"));
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                sb.Append(" AND (lower(Description) LIKE @q OR lower(Source) LIKE @q)");
                cmd.Parameters.AddWithValue("@q", "%" + search.Trim().ToLower() + "%");
            }

            sb.Append(" ORDER BY date(Date) DESC, Id DESC;");
            cmd.CommandText = sb.ToString();

            var dt = new DataTable();
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }

        /// <summary>
        /// Prostszy insert przychodów – mapuje parametr note na Description.
        /// U¿ywany m.in. przez ekran „Dodaj”.
        /// </summary>
        public static void InsertIncome(int userId, decimal amount, DateTime date, string source, string? note)
        {
            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Incomes(UserId, Amount, Date, Description, Source)
VALUES (@u, @a, @d, @desc, @s);";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(date));
            cmd.Parameters.AddWithValue("@desc", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@s", source ?? "Przychody");
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Nowe przeci¹¿enie pod AddExpensePage – wersja z kategori¹.
        /// Tabela Incomes nie ma CategoryId, wiêc parametr jest na razie ignorowany,
        /// ale sygnatura zgadza siê z wywo³aniami w UI.
        /// </summary>
        public static void InsertIncome(
            int userId,
            decimal amount,
            DateTime date,
            int? categoryId,
            string source,
            string? note)
        {
            InsertIncome(userId, amount, date, source, note);
        }

        // ===== PRZYCHODY – operacje na Ÿród³ach =====

        /// <summary>
        /// Przychód do wolnej gotówki.
        /// Zwiêksza Incomes + CashOnHand. SavedCash zostaje bez zmian.
        /// </summary>
        public static void AddIncomeToFreeCash(
            int userId,
            decimal amount,
            DateTime date,
            int? categoryId,
            string source,
            string? note)
        {
            if (amount <= 0) return;

            InsertIncome(userId, amount, date, categoryId, source, note);

            var allCash = GetCashOnHand(userId);
            SetCashOnHand(userId, allCash + amount);
        }

        /// <summary>
        /// Przychód do od³o¿onej gotówki.
        /// Zwiêksza Incomes + CashOnHand + SavedCash o tê sam¹ kwotê.
        /// </summary>
        public static void AddIncomeToSavedCash(
            int userId,
            decimal amount,
            DateTime date,
            int? categoryId,
            string source,
            string? note)
        {
            if (amount <= 0) return;

            InsertIncome(userId, amount, date, categoryId, source, note);

            var allCash = GetCashOnHand(userId);
            SetCashOnHand(userId, allCash + amount);

            AddToSavedCash(userId, amount);
        }

        /// <summary>
        /// Przychód bezpoœrednio na konto bankowe.
        /// Zmniejsza Incomes oraz saldo wskazanego rachunku.
        /// </summary>
        public static void AddIncomeToBankAccount(
            int userId,
            int accountId,
            decimal amount,
            DateTime date,
            int? categoryId,
            string source,
            string? note)
        {
            if (amount <= 0) return;

            InsertIncome(userId, amount, date, categoryId, source, note);

            using var c = OpenAndEnsureSchema();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE BankAccounts
   SET Balance = Balance + @a
 WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@id", accountId);
            cmd.Parameters.AddWithValue("@u", userId);

            var rows = cmd.ExecuteNonQuery();
            if (rows == 0)
                throw new InvalidOperationException("Nie znaleziono rachunku bankowego lub nie nale¿y do u¿ytkownika.");
        }
    }
}
