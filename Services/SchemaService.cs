using System;
using Microsoft.Data.Sqlite;

namespace Finly.Services
{
    public static class SchemaService
    {
        private static readonly object _schemaLock = new();

        public static void Ensure(SqliteConnection con)
        {
            if (con == null) throw new ArgumentNullException(nameof(con));

            lock (_schemaLock)
            {
                // Bezpieczne PRAGMA
                using (var p = con.CreateCommand())
                {
                    p.CommandText = @"PRAGMA foreign_keys = ON;
                                      PRAGMA busy_timeout = 5000;
                                      PRAGMA journal_mode = WAL;";
                    p.ExecuteNonQuery();
                }

                using var tx = con.BeginTransaction();

                SqliteCommand Cmd(string sql)
                {
                    var c = con.CreateCommand();
                    c.Transaction = tx;
                    c.CommandText = sql;
                    return c;
                }

                bool ColExists(string table, string col) => ColumnExists(con, tx, table, col);

                // ===== Tabele (idempotentnie) =====
                using (var cmd = Cmd(@"
CREATE TABLE IF NOT EXISTS Users(
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Username        TEXT NOT NULL UNIQUE,
    PasswordHash    TEXT NOT NULL,
    Email           TEXT NULL,
    FirstName       TEXT NULL,
    LastName        TEXT NULL,
    Address         TEXT NULL,
    AccountType     TEXT NULL,
    CompanyName     TEXT NULL,
    NIP             TEXT NULL,
    REGON           TEXT NULL,
    KRS             TEXT NULL,
    CompanyAddress  TEXT NULL,
    CreatedAt       TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS Categories(
    Id      INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId  INTEGER NULL,
    Name    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Expenses(
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NOT NULL,
    Date        TEXT    NOT NULL,
    Amount      REAL    NOT NULL,
    Title       TEXT    NULL,
    Description TEXT    NULL,
    CategoryId  INTEGER NULL,
    AccountId   INTEGER NULL,
    Note        TEXT    NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS BankConnections(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    BankName      TEXT NOT NULL,
    AccountHolder TEXT NOT NULL,
    Status        TEXT NOT NULL,
    LastSync      TEXT
);

-- Uwaga: docelowa definicja BankAccounts ma ConnectionId NULL i ON DELETE SET NULL
CREATE TABLE IF NOT EXISTS BankAccounts(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ConnectionId INTEGER NULL,
    UserId       INTEGER NOT NULL,
    AccountName  TEXT NOT NULL,
    Iban         TEXT NOT NULL,
    Currency     TEXT NOT NULL,
    Balance      NUMERIC NOT NULL DEFAULT 0,
    LastSync     TEXT,
    FOREIGN KEY(UserId)       REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(ConnectionId) REFERENCES BankConnections(Id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS PersonalProfiles(
    UserId       INTEGER PRIMARY KEY,
    FirstName    TEXT NULL,
    LastName     TEXT NULL,
    Address      TEXT NULL,
    BirthDate    TEXT NULL,
    City         TEXT NULL,
    PostalCode   TEXT NULL,
    HouseNo      TEXT NULL,
    CreatedAt    TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS CompanyProfiles(
    UserId          INTEGER PRIMARY KEY,
    CompanyName     TEXT NULL,
    NIP             TEXT NULL,
    REGON           TEXT NULL,
    KRS             TEXT NULL,
    CompanyAddress  TEXT NULL,
    CreatedAt       TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);
"))
                {
                    cmd.ExecuteNonQuery();
                }

                // ===== Migracje (idempotentne) =====
                // Users
                AddColumnIfMissing(con, tx, "Users", "AccountType", "TEXT", "DEFAULT 'Personal'");
                AddColumnIfMissing(con, tx, "Users", "CompanyName", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "NIP", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "REGON", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "KRS", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "CompanyAddress", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "Email", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "FirstName", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "LastName", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "Address", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "CreatedAt", "TEXT", "NOT NULL DEFAULT CURRENT_TIMESTAMP");

                // Categories
                AddColumnIfMissing(con, tx, "Categories", "UserId", "INTEGER NULL");

                // Expenses – spójne z zapytaniami w UI
                AddColumnIfMissing(con, tx, "Expenses", "Title", "TEXT");
                AddColumnIfMissing(con, tx, "Expenses", "Description", "TEXT");
                AddColumnIfMissing(con, tx, "Expenses", "CategoryId", "INTEGER NULL");
                AddColumnIfMissing(con, tx, "Expenses", "AccountId", "INTEGER NULL");
                AddColumnIfMissing(con, tx, "Expenses", "Note", "TEXT");

                // Backfill: Title = Description, jeśli Title puste
                if (ColExists("Expenses", "Title") && ColExists("Expenses", "Description"))
                {
                    using var backfill = Cmd(
                        "UPDATE Expenses SET Title = COALESCE(Title, Description) " +
                        "WHERE Title IS NULL AND Description IS NOT NULL;");
                    backfill.ExecuteNonQuery();
                }

                tx.Commit();

                // Po CREATE/ALTER z FK: upewnij się, że BankAccounts ma właściwy układ.
                Ensure_BankAccounts_ConnectionId_Nullable_And_FK(con);

                // ===== Indeksy (po migracji) =====
                using var idx = con.CreateCommand();
                idx.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Username_NC
    ON Users(Username COLLATE NOCASE);

CREATE INDEX IF NOT EXISTS IX_Categories_User_Name
    ON Categories(COALESCE(UserId,0), Name COLLATE NOCASE);

CREATE INDEX IF NOT EXISTS IX_Expenses_User_Date
    ON Expenses(UserId, Date);

CREATE INDEX IF NOT EXISTS IX_Expenses_User_Category
    ON Expenses(UserId, CategoryId);";
                idx.ExecuteNonQuery();
            }
        }

        // ===== Helpers =====
        private static bool ColumnExists(SqliteConnection con, SqliteTransaction tx, string table, string column)
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA table_info('{table}');";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void AddColumnIfMissing(SqliteConnection con, SqliteTransaction tx,
                                               string table, string column, string sqlType, string extra = "")
        {
            if (!ColumnExists(con, tx, table, column))
            {
                using var alter = con.CreateCommand();
                alter.Transaction = tx;
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType} {extra};";
                alter.ExecuteNonQuery();
            }
        }

        // --- Migracja BankAccounts do: ConnectionId NULL + ON DELETE SET NULL ---

        private static bool ColumnIsNotNull(SqliteConnection c, string table, string column)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{table}');";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return r.GetInt32(3) == 1; // notnull=1
            }
            return false;
        }

        private static void Ensure_BankAccounts_ConnectionId_Nullable_And_FK(SqliteConnection c)
        {
            // Jeśli już jest NULLABLE – przyjmujemy, że FK jest poprawny (zdefiniowany jak w docelowej CREATE TABLE)
            if (!ColumnIsNotNull(c, "BankAccounts", "ConnectionId")) return;

            using var t = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;

            cmd.CommandText = "PRAGMA foreign_keys=OFF;"; cmd.ExecuteNonQuery();

            cmd.CommandText = @"
CREATE TABLE BankAccounts_new (
  Id           INTEGER PRIMARY KEY AUTOINCREMENT,
  ConnectionId INTEGER NULL,
  UserId       INTEGER NOT NULL,
  AccountName  TEXT NOT NULL,
  Iban         TEXT NOT NULL,
  Currency     TEXT NOT NULL,
  Balance      NUMERIC NOT NULL DEFAULT 0,
  LastSync     TEXT,
  FOREIGN KEY(UserId)       REFERENCES Users(Id) ON DELETE CASCADE,
  FOREIGN KEY(ConnectionId) REFERENCES BankConnections(Id) ON DELETE SET NULL
);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
INSERT INTO BankAccounts_new (Id, ConnectionId, UserId, AccountName, Iban, Currency, Balance, LastSync)
SELECT Id, ConnectionId, UserId, AccountName, Iban, Currency, Balance, LastSync
FROM BankAccounts;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "DROP TABLE BankAccounts;"; cmd.ExecuteNonQuery();
            cmd.CommandText = "ALTER TABLE BankAccounts_new RENAME TO BankAccounts;"; cmd.ExecuteNonQuery();

            cmd.CommandText = "PRAGMA foreign_keys=ON;"; cmd.ExecuteNonQuery();
            t.Commit();
        }
    }
}










