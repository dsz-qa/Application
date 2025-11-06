// Finly/Services/DatabaseService.cs
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Finly.Models;

namespace Finly.Services
{
    /// Centralny serwis SQLite (Microsoft.Data.Sqlite)
    public static class DatabaseService
    {
        // ====== konfiguracja / locki ======
        private static readonly object _schemaLock = new();
        private static bool _schemaInitialized = false;

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

        // ====== po³¹czenie ======
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

        /// Alias – niektóre pliki mog¹ go wo³aæ
        public static SqliteConnection GetOpenConnection() => GetConnection();

        /// Upewnij siê, ¿e schemat jest gotowy (wywo³uj na starcie)
        public static void EnsureTables()
        {
            lock (_schemaLock)
            {
                using var con = GetConnection();
                SchemaService.Ensure(con);
                _schemaInitialized = true;
            }
        }

        /// Otwiera po³¹czenie i leniwie zapewnia schemat
        private static SqliteConnection OpenAndEnsureSchema()
        {
            if (!_schemaInitialized)
            {
                lock (_schemaLock)
                {
                    if (!_schemaInitialized)
                    {
                        using var c = GetConnection();
                        SchemaService.Ensure(c);
                        _schemaInitialized = true;
                    }
                }
            }
            return GetConnection();
        }

        private static string ToIsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd");

        // ====== HELPERY odczytu ======
        private static string? GetNullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        private static string GetStringSafe(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        private static DateTime GetDate(SqliteDataReader r, int i)
        {
            if (r.IsDBNull(i)) return DateTime.MinValue;
            var v = r.GetValue(i);
            if (v is DateTime dt) return dt;
            return DateTime.TryParse(v?.ToString(), out var p) ? p : DateTime.MinValue;
        }

        // =================================================================
        // ======================  WYDATKI  =================================
        // =================================================================

        public static List<ExpenseDisplayModel> GetExpensesWithCategoryNameByUser(int userId)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
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
                    Amount = r.GetDouble(2), // model u¿ywa double
                    Date = GetDate(r, 3),
                    Description = GetNullableString(r, 4) ?? string.Empty,
                    CategoryName = GetStringSafe(r, 5)
                });
            }
            return list;
        }

        public static List<ExpenseDisplayModel> GetExpensesWithCategory()
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
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

        public static Expense? GetExpenseById(int id)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Id, UserId, Amount, Date, Description, CategoryId
FROM Expenses WHERE Id=@id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var cat = r.IsDBNull(5) ? 0 : r.GetInt32(5); // 0 = brak
            return new Expense
            {
                Id = r.GetInt32(0),
                UserId = r.GetInt32(1),
                Amount = r.GetDouble(2),
                Date = GetDate(r, 3),
                Description = GetNullableString(r, 4),
                CategoryId = cat
            };
        }

        public static int InsertExpense(Expense e)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Expenses(UserId, Amount, Date, Description, CategoryId)
VALUES (@u,@a,@d,@desc,@c);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", e.UserId);
            cmd.Parameters.AddWithValue("@a", e.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);
            if (e.CategoryId > 0) cmd.Parameters.AddWithValue("@c", e.CategoryId);
            else cmd.Parameters.AddWithValue("@c", DBNull.Value);

            var rowId = (long)(cmd.ExecuteScalar() ?? 0L);
            return (int)rowId;
        }

        public static void UpdateExpense(Expense e)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE Expenses SET
    UserId=@u, Amount=@a, Date=@d, Description=@desc, CategoryId=@c
WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", e.Id);
            cmd.Parameters.AddWithValue("@u", e.UserId);
            cmd.Parameters.AddWithValue("@a", e.Amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(e.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);
            if (e.CategoryId > 0) cmd.Parameters.AddWithValue("@c", e.CategoryId);
            else cmd.Parameters.AddWithValue("@c", DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public static void DeleteExpense(int id)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Expenses WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // =================================================================
        // ======================  KATEGORIE  ===============================
        // =================================================================

        public static List<string> GetCategoriesByUser(int userId)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Categories WHERE UserId=@u ORDER BY Name;";
            cmd.Parameters.AddWithValue("@u", userId);

            var names = new List<string>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) names.Add(r.GetString(0));
            return names;
        }

        public static List<Category> GetCategories(int userId)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id,UserId,Name FROM Categories WHERE UserId=@u ORDER BY Name;";
            cmd.Parameters.AddWithValue("@u", userId);

            var list = new List<Category>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Category
                {
                    Id = r.GetInt32(0),
                    UserId = r.GetInt32(1),
                    Name = r.GetString(2)
                });
            }
            return list;
        }
        // G³ówna wersja
        public static int GetOrCreateCategoryId(int userId, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Nazwa kategorii pusta.", nameof(name));

            var existing = GetCategoryIdByName(userId, name);
            return existing ?? CreateCategory(userId, name);
        }

        // *** PRZECI¥¯ENIE DLA ZGODNOŒCI Z DOTYCHCZASOWYMI WYWO£ANIAMI (string, int) ***
        public static int GetOrCreateCategoryId(string name, int userId)
            => GetOrCreateCategoryId(userId, name);



        public static int? GetCategoryIdByName(int userId, string name)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id FROM Categories WHERE UserId=@u AND lower(Name)=lower(@n) LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name.Trim());

            var obj = cmd.ExecuteScalar();
            return (obj == null || obj == DBNull.Value) ? (int?)null : Convert.ToInt32(obj);
        }

        public static int CreateCategory(int userId, string name)
        {
            using var con = OpenAndEnsureSchema();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Categories(UserId, Name) VALUES (@u,@n);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", name.Trim());

            var rowId = (long)(cmd.ExecuteScalar() ?? 0L);
            return (int)rowId;
        }

        // Wygodny wrapper dopasowany do wywo³añ z UI (nic nie zwraca)
        public static void AddExpense(Expense e)
        {
            using var con = GetConnection();           // albo OpenAndEnsureSchema() jeœli u¿ywasz leniwej inicjalizacji
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Expenses(UserId, Amount, Date, Description, CategoryId)
VALUES (@u, @a, @d, @desc, @c);";
            cmd.Parameters.AddWithValue("@u", e.UserId);
            cmd.Parameters.AddWithValue("@a", e.Amount);               // double
            cmd.Parameters.AddWithValue("@d", e.Date);                 // DateTime – SQLite TEXT/NUMERIC OK
            cmd.Parameters.AddWithValue("@desc", (object?)e.Description ?? DBNull.Value);

            // Je¿eli CategoryId to int? – wstaw NULL; je¿eli int i 0 oznacza „brak”, te¿ wstaw NULL
            if (e.CategoryId is int cid && cid > 0) cmd.Parameters.AddWithValue("@c", cid);
            else cmd.Parameters.AddWithValue("@c", DBNull.Value);

            cmd.ExecuteNonQuery();
        }

    }
}



