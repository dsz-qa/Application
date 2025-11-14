using Finly.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Finly.Services
{
    /// Proste zarz¹dzanie u¿ytkownikami + stan „kto zalogowany”.
    public static class UserService
    {
        // ===== Stan logowania =====
        public static int CurrentUserId { get; set; } = 0;
        public static string? CurrentUserName { get; set; }
        public static string? CurrentUserEmail { get; set; }

        public static int GetCurrentUserId() => CurrentUserId;

        public static void SetCurrentUser(string username)
        {
            CurrentUserName = username;
            CurrentUserId = GetUserIdByUsername(username);
            CurrentUserEmail = GetEmail(CurrentUserId);
        }

        public static void ClearCurrentUser()
        {
            CurrentUserId = 0;
            CurrentUserName = null;
            CurrentUserEmail = null;
        }

        public static bool IsOnboarded(int userId)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(IsOnboarded,0) FROM Users WHERE Id=@id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", userId);
            var obj = cmd.ExecuteScalar();
            return obj != null && obj != DBNull.Value && Convert.ToInt32(obj) != 0;
        }

        public static void MarkOnboarded(int userId)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Users SET IsOnboarded = 1 WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.ExecuteNonQuery();
        }


        // ===== Typ konta =====
        public static AccountType GetAccountType(int userId)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT AccountType FROM Users WHERE Id=@id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", userId);
            var raw = cmd.ExecuteScalar()?.ToString();
            return string.Equals(raw, "Business", StringComparison.OrdinalIgnoreCase)
                 ? AccountType.Business
                 : AccountType.Personal;
        }

        // ===== Rejestracja / logowanie =====
        public static bool Register(string username, string password)
            => Register(username, password, AccountType.Personal, null, null, null, null, null);

        public static bool Register(
            string username,
            string password,
            AccountType accountType,
            string? companyName,
            string? nip,
            string? regon,
            string? krs,
            string? companyAddress)
        {
            var u = Normalize(username);
            if (u is null || string.IsNullOrWhiteSpace(password)) return false;

            using var con = DatabaseService.GetConnection();
            SchemaService.Ensure(con);
            EnsureEmailUniqueIndex(con); // idempotentnie

            // unikalnoœæ loginu
            using (var check = con.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM Users WHERE lower(Username)=lower($u) LIMIT 1;";
                check.Parameters.AddWithValue("$u", u);
                var exists = check.ExecuteScalar();
                if (exists != null && exists != DBNull.Value) return false;
            }

            using (var ins = con.CreateCommand())
            {
                ins.CommandText = @"
INSERT INTO Users (Username, PasswordHash, AccountType, CompanyName, NIP, REGON, KRS, CompanyAddress)
VALUES ($u, $ph, $type, $cname, $nip, $regon, $krs, $caddr);";
                ins.Parameters.AddWithValue("$u", u);
                ins.Parameters.AddWithValue("$ph", HashPassword(password));
                ins.Parameters.AddWithValue("$type", accountType == AccountType.Business ? "Business" : "Personal");
                ins.Parameters.AddWithValue("$cname", (object?)companyName ?? DBNull.Value);
                ins.Parameters.AddWithValue("$nip", (object?)nip ?? DBNull.Value);
                ins.Parameters.AddWithValue("$regon", (object?)regon ?? DBNull.Value);
                ins.Parameters.AddWithValue("$krs", (object?)krs ?? DBNull.Value);
                ins.Parameters.AddWithValue("$caddr", (object?)companyAddress ?? DBNull.Value);
                return ins.ExecuteNonQuery() == 1;
            }
        }

        public static bool IsUsernameAvailable(string username)
        {
            var u = Normalize(username);
            if (u is null) return false;

            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Users WHERE lower(Username)=lower($u) LIMIT 1;";
            cmd.Parameters.AddWithValue("$u", u);
            var exists = cmd.ExecuteScalar();
            return exists is null || exists == DBNull.Value;
        }

        /// Logowanie po **loginie lub e-mailu** (case-insensitive).
        public static bool Login(string login, string password)
        {
            var u = Normalize(login);
            if (u is null || string.IsNullOrWhiteSpace(password)) return false;

            using var con = DatabaseService.GetConnection();
            EnsureEmailUniqueIndex(con); // idempotentne

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Username, Email, PasswordHash
FROM Users
WHERE lower(Username)=lower($u) OR lower(Email)=lower($u)
LIMIT 1;";
            cmd.Parameters.AddWithValue("$u", u);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return false;

            var id = r.GetInt32(0);
            var un = r.GetString(1);
            var em = r.IsDBNull(2) ? null : r.GetString(2);
            var ph = r.GetString(3);

            if (!VerifyPassword(password, ph)) return false;

            CurrentUserId = id;
            CurrentUserName = un;
            CurrentUserEmail = em;
            return true;
        }

        public static int GetUserIdByUsername(string username)
        {
            var u = Normalize(username) ?? string.Empty;
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id FROM Users WHERE lower(Username)=lower($u) LIMIT 1;";
            cmd.Parameters.AddWithValue("$u", u);
            var obj = cmd.ExecuteScalar();
            return (obj is null || obj == DBNull.Value) ? -1 : Convert.ToInt32(obj);
        }

        /// NOWE: Id po „login **albo** e-mail”.
        public static int GetUserIdByLogin(string login)
        {
            var u = Normalize(login) ?? string.Empty;
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Id
FROM Users
WHERE lower(Username)=lower($u) OR lower(Email)=lower($u)
LIMIT 1;";
            cmd.Parameters.AddWithValue("$u", u);
            var obj = cmd.ExecuteScalar();
            return (obj is null || obj == DBNull.Value) ? -1 : Convert.ToInt32(obj);
        }

        // ===== Has³o =====
        public static bool ChangePassword(int userId, string oldPassword, string newPassword)
        {
            using var c = DatabaseService.GetConnection();

            string currentHash = "";
            using (var get = c.CreateCommand())
            {
                get.CommandText = "SELECT PasswordHash FROM Users WHERE Id=@id;";
                get.Parameters.AddWithValue("@id", userId);
                currentHash = get.ExecuteScalar()?.ToString() ?? "";
            }

            if (!string.Equals(currentHash, HashPassword(oldPassword), StringComparison.Ordinal))
                return false;

            using (var upd = c.CreateCommand())
            {
                upd.CommandText = "UPDATE Users SET PasswordHash=@h WHERE Id=@id;";
                upd.Parameters.AddWithValue("@h", HashPassword(newPassword));
                upd.Parameters.AddWithValue("@id", userId);
                upd.ExecuteNonQuery();
            }
            return true;
        }

        // ===== Dane podstawowe =====
        public static string GetUsername(int userId) => GetUserById(userId)?.Username ?? "";
        public static string GetEmail(int userId) => GetUserById(userId)?.Email ?? "";

        public static void UpdateEmail(int userId, string email)
        {
            using var c = DatabaseService.GetConnection();
            EnsureEmailUniqueIndex(c);

            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Email=@e WHERE Id=@id;";
            var norm = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            cmd.Parameters.AddWithValue("@e", (object?)norm ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.ExecuteNonQuery();

            if (CurrentUserId == userId) CurrentUserEmail = norm;
        }


        public static DateTime GetCreatedAt(int userId)
            => GetUserById(userId)?.CreatedAt ?? DateTime.MinValue;

        public static (int Id, string Username, string? Email, DateTime CreatedAt)? GetUserById(int userId)
        {
            using var c = DatabaseService.GetConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, Email, CreatedAt FROM Users WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", userId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            DateTime createdAt;
            var raw = r.GetValue(3);
            if (raw is DateTime dt) createdAt = dt;
            else if (DateTime.TryParse(raw?.ToString(), out var parsed)) createdAt = parsed;
            else createdAt = DateTime.MinValue;

            return (r.GetInt32(0),
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    createdAt);
        }

        // ===== Usuwanie konta =====
        public static bool DeleteAccount(int userId)
        {
            try
            {
                using var con = DatabaseService.GetConnection();
                using var tx = con.BeginTransaction();

                void Exec(string sql)
                {
                    using var cmd = con.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.Parameters.AddWithValue("@id", userId);
                    cmd.ExecuteNonQuery();
                }

                Exec("DELETE FROM Expenses        WHERE UserId=@id;");
                Exec("DELETE FROM Categories      WHERE UserId=@id;");
                Exec("DELETE FROM BankAccounts    WHERE UserId=@id;");
                Exec("DELETE FROM BankConnections WHERE UserId=@id;");
                Exec("DELETE FROM PersonalProfiles WHERE UserId=@id;");
                Exec("DELETE FROM CompanyProfiles  WHERE UserId=@id;");

                using (var del = con.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM Users WHERE Id=@id;";
                    del.Parameters.AddWithValue("@id", userId);
                    if (del.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Nie znaleziono u¿ytkownika.");
                }

                tx.Commit();
                if (CurrentUserId == userId) ClearCurrentUser();
                return true;
            }
            catch { return false; }
        }

        // ===== Profil (miks/zgodnoœæ) =====
        public static UserProfile GetProfile(int userId)
        {
            using var c = DatabaseService.GetConnection();

            string? firstName = null, lastName = null, address = null;
            string? birthYear = null, city = null, postalCode = null, houseNo = null;

            try
            {
                using var p = c.CreateCommand();
                p.CommandText = @"
SELECT FirstName, LastName, Address, BirthDate, City, PostalCode, HouseNo
FROM PersonalProfiles WHERE UserId=@id LIMIT 1;";
                p.Parameters.AddWithValue("@id", userId);

                using var pr = p.ExecuteReader();
                if (pr.Read())
                {
                    firstName = pr.IsDBNull(0) ? null : pr.GetString(0);
                    lastName = pr.IsDBNull(1) ? null : pr.GetString(1);
                    address = pr.IsDBNull(2) ? null : pr.GetString(2);
                    if (!pr.IsDBNull(3))
                    {
                        var d = pr.GetString(3);
                        if (DateTime.TryParse(d, out var bd)) birthYear = bd.Year.ToString();
                    }
                    city = pr.IsDBNull(4) ? null : pr.GetString(4);
                    postalCode = pr.IsDBNull(5) ? null : pr.GetString(5);
                    houseNo = pr.IsDBNull(6) ? null : pr.GetString(6);
                }
            }
            catch { /* brak tabeli – OK */ }

            string? companyName = null, companyNip = null, companyAddress = null;
            using (var u = c.CreateCommand())
            {
                u.CommandText = @"
SELECT FirstName, LastName, Address,
       CompanyName,
       COALESCE(NIP, CompanyNip) as NipCompat,
       CompanyAddress
FROM Users WHERE Id=@id;";
                u.Parameters.AddWithValue("@id", userId);
                using var ur = u.ExecuteReader();
                if (ur.Read())
                {
                    firstName ??= ur.IsDBNull(0) ? null : ur.GetString(0);
                    lastName ??= ur.IsDBNull(1) ? null : ur.GetString(1);
                    address ??= ur.IsDBNull(2) ? null : ur.GetString(2);

                    companyName = ur.IsDBNull(3) ? null : ur.GetString(3);
                    companyNip = ur.IsDBNull(4) ? null : ur.GetString(4);
                    companyAddress = ur.IsDBNull(5) ? null : ur.GetString(5);
                }
            }

            return new UserProfile
            {
                FirstName = firstName,
                LastName = lastName,
                Address = address,
                BirthYear = birthYear,
                City = city,
                PostalCode = postalCode,
                HouseNo = houseNo,

                CompanyName = companyName,
                CompanyNip = companyNip,
                CompanyAddress = companyAddress
            };
        }

        public static void UpdateProfile(int userId, UserProfile p)
        {
            using var c = DatabaseService.GetConnection();

            try
            {
                using (var up = c.CreateCommand())
                {
                    up.CommandText = @"
INSERT INTO PersonalProfiles (UserId, FirstName, LastName, Address, City, PostalCode, HouseNo, CreatedAt)
VALUES (@id, @fn, @ln, @addr, @city, @pc, @house, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE SET
    FirstName=@fn, LastName=@ln, Address=@addr, City=@city, PostalCode=@pc, HouseNo=@house;";
                    up.Parameters.AddWithValue("@id", userId);
                    up.Parameters.AddWithValue("@fn", (object?)p.FirstName ?? DBNull.Value);
                    up.Parameters.AddWithValue("@ln", (object?)p.LastName ?? DBNull.Value);
                    up.Parameters.AddWithValue("@addr", (object?)p.Address ?? DBNull.Value);
                    up.Parameters.AddWithValue("@city", (object?)p.City ?? DBNull.Value);
                    up.Parameters.AddWithValue("@pc", (object?)p.PostalCode ?? DBNull.Value);
                    up.Parameters.AddWithValue("@house", (object?)p.HouseNo ?? DBNull.Value);
                    up.ExecuteNonQuery();
                }

                if (int.TryParse(p.BirthYear, out var y) && y >= 1900 && y <= DateTime.Now.Year)
                {
                    using var upBirth = c.CreateCommand();
                    upBirth.CommandText = @"UPDATE PersonalProfiles
                                            SET BirthDate = date(@iso,'start of year')
                                            WHERE UserId=@id;";
                    upBirth.Parameters.AddWithValue("@iso", $"{y}-01-01");
                    upBirth.Parameters.AddWithValue("@id", userId);
                    upBirth.ExecuteNonQuery();
                }
            }
            catch
            {
                using var upu = c.CreateCommand();
                upu.CommandText = @"UPDATE Users SET FirstName=@fn, LastName=@ln, Address=@addr WHERE Id=@id;";
                upu.Parameters.AddWithValue("@fn", (object?)p.FirstName ?? DBNull.Value);
                upu.Parameters.AddWithValue("@ln", (object?)p.LastName ?? DBNull.Value);
                upu.Parameters.AddWithValue("@addr", (object?)p.Address ?? DBNull.Value);
                upu.Parameters.AddWithValue("@id", userId);
                upu.ExecuteNonQuery();
            }

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE Users SET
    CompanyName    = @cname,
    NIP            = @nip,
    CompanyAddress = @caddr
WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@cname", (object?)p.CompanyName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@nip", (object?)p.CompanyNip ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@caddr", (object?)p.CompanyAddress ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", userId);
                cmd.ExecuteNonQuery();
            }
        }

        // ===== Dane osobowe w Users =====
        private static void EnsurePersonalColumns(SqliteConnection con)
        {
            bool Has(string col)
            {
                using var c = con.CreateCommand();
                c.CommandText = "PRAGMA table_info([Users]);";
                using var r = c.ExecuteReader();
                while (r.Read())
                    if (string.Equals(r["name"]?.ToString(), col, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
            void Add(string col, string sqlType)
            {
                using var c = con.CreateCommand();
                c.CommandText = $"ALTER TABLE Users ADD COLUMN {col} {sqlType};";
                c.ExecuteNonQuery();
            }

            if (!Has("Email")) Add("Email", "TEXT NULL");
            if (!Has("FirstName")) Add("FirstName", "TEXT NULL");
            if (!Has("LastName")) Add("LastName", "TEXT NULL");
            if (!Has("BirthDate")) Add("BirthDate", "TEXT NULL");
            if (!Has("BirthYear")) Add("BirthYear", "INTEGER NULL");
            if (!Has("City")) Add("City", "TEXT NULL");
            if (!Has("PostalCode")) Add("PostalCode", "TEXT NULL");
            if (!Has("HouseNo")) Add("HouseNo", "TEXT NULL");
        }

        public static PersonalDetails GetPersonalDetails(int userId)
        {
            using var con = DatabaseService.GetConnection();
            EnsurePersonalColumns(con);

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Email, FirstName, LastName, BirthDate, BirthYear, City, PostalCode, HouseNo
FROM Users WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", userId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return new PersonalDetails();

            DateTime? birthDate = null;
            if (!r.IsDBNull(3))
            {
                var raw = r.GetValue(3)?.ToString();
                if (DateTime.TryParse(raw, out var dt)) birthDate = dt;
            }
            if (birthDate is null && !r.IsDBNull(4))
            {
                var raw = r.GetValue(4)?.ToString();
                if (int.TryParse(raw, out var by) && by >= 1900 && by <= DateTime.Now.Year)
                    birthDate = new DateTime(by, 1, 1);
            }

            return new PersonalDetails
            {
                Email = r.IsDBNull(0) ? null : r.GetString(0),
                FirstName = r.IsDBNull(1) ? null : r.GetString(1),
                LastName = r.IsDBNull(2) ? null : r.GetString(2),
                BirthDate = birthDate,
                City = r.IsDBNull(5) ? null : r.GetString(5),
                PostalCode = r.IsDBNull(6) ? null : r.GetString(6),
                HouseNo = r.IsDBNull(7) ? null : r.GetString(7)
            };
        }

        public static void UpdatePersonalDetails(int userId, PersonalDetails d)
        {
            using var con = DatabaseService.GetConnection();
            EnsurePersonalColumns(con);
            EnsureEmailUniqueIndex(con);

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE Users SET
    Email      = @email,
    FirstName  = @fn,
    LastName   = @ln,
    BirthDate  = @bd,
    BirthYear  = @by,
    City       = @city,
    PostalCode = @pc,
    HouseNo    = @hn
WHERE Id=@id;";

            cmd.Parameters.AddWithValue("@id", userId);
            cmd.Parameters.AddWithValue("@email", (object?)d.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fn", (object?)d.FirstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ln", (object?)d.LastName ?? DBNull.Value);

            if (d.BirthDate.HasValue)
            {
                cmd.Parameters.AddWithValue("@bd", d.BirthDate.Value.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@by", d.BirthDate.Value.Year);
            }
            else
            {
                cmd.Parameters.AddWithValue("@bd", DBNull.Value);
                cmd.Parameters.AddWithValue("@by", DBNull.Value);
            }

            cmd.Parameters.AddWithValue("@city", (object?)d.City ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pc", (object?)d.PostalCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hn", (object?)d.HouseNo ?? DBNull.Value);

            cmd.ExecuteNonQuery();

            if (CurrentUserId == userId)
                CurrentUserEmail = d.Email;
        }

        // ===== Pomocnicze =====
        private static string? Normalize(string username)
            => string.IsNullOrWhiteSpace(username) ? null : username.Trim();

        private static string HashPassword(string password)
            => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password ?? "")));

        private static bool VerifyPassword(string password, string storedBase64Sha256)
            => string.Equals(HashPassword(password), storedBase64Sha256, StringComparison.Ordinal);

        /// Unikalny e-mail (case-insensitive), tylko gdy Email nie jest NULL.
        private static void EnsureEmailUniqueIndex(SqliteConnection con)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Email_NC
ON Users(Email COLLATE NOCASE)
WHERE Email IS NOT NULL;";
            cmd.ExecuteNonQuery();
        }
    }
}






