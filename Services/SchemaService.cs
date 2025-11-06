using System;
using Microsoft.Data.Sqlite;

namespace Finly.Services
{
    public static class SchemaService
    {
        private static readonly object _schemaLock = new();

        public static void Ensure(SqliteConnection con)
        {
            if (con is null) throw new ArgumentNullException(nameof(con));

            lock (_schemaLock)
            {
                using (var p = con.CreateCommand())
                {
                    p.CommandText = @"PRAGMA busy_timeout = 5000;
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

                bool ColTx(string table, string col) => ColumnExists(con, tx, table, col);

                using (var cmd = Cmd(@"
CREATE TABLE IF NOT EXISTS Users(
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Username        TEXT NOT NULL UNIQUE,
    PasswordHash    TEXT NOT NULL,
    Email           TEXT NULL,
    FirstName       TEXT NULL,
    LastName        TEXT NULL,
    Address         TEXT NULL,
    CompanyName     TEXT NULL,
    CompanyNip      TEXT NULL, -- stara kolumna (zgodność)
    CompanyAddress  TEXT NULL,
    AccountType     TEXT NULL,
    NIP             TEXT NULL,
    REGON           TEXT NULL,
    KRS             TEXT NULL,
    CreatedAt       TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS Categories(
    Id     INTEGER PRIMARY KEY AUTOINCREMENT,
    Name   TEXT NOT NULL,
    UserId INTEGER NULL
);

CREATE TABLE IF NOT EXISTS Expenses(
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Amount      REAL    NOT NULL,
    CategoryId  INTEGER NULL,
    Date        TEXT    NOT NULL,
    Description TEXT    NULL,
    UserId      INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS BankConnections(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    BankName      TEXT NOT NULL,
    AccountHolder TEXT NOT NULL,
    Status        TEXT NOT NULL,
    LastSync      TEXT
);

CREATE TABLE IF NOT EXISTS BankAccounts(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ConnectionId INTEGER NOT NULL,
    UserId       INTEGER NOT NULL,
    AccountName  TEXT NOT NULL,
    Iban         TEXT NOT NULL,
    Currency     TEXT NOT NULL,
    Balance      NUMERIC NOT NULL DEFAULT 0,
    LastSync     TEXT,
    FOREIGN KEY(ConnectionId) REFERENCES BankConnections(Id)
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

                // ---- migracje idempotentne ----
                void AddColumnIfMissing(string table, string column, string sqlType, string defaultClause = "")
                {
                    if (!ColumnExists(con, table, column)) // <- przeciążenie bez tx
                    {
                        using var alter = con.CreateCommand();
                        alter.Transaction = tx;
                        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType} {defaultClause};";
                        alter.ExecuteNonQuery();
                    }
                }

                AddColumnIfMissing("Users", "AccountType", "TEXT", "DEFAULT 'Personal'");
                AddColumnIfMissing("Users", "CompanyName", "TEXT");
                AddColumnIfMissing("Users", "NIP", "TEXT");
                AddColumnIfMissing("Users", "REGON", "TEXT");
                AddColumnIfMissing("Users", "KRS", "TEXT");
                AddColumnIfMissing("Users", "CompanyAddress", "TEXT");

                // zgodność wstecz: CompanyNip -> NIP
                bool hasCompanyNip = ColTx("Users", "CompanyNip");
                bool hasNip = ColTx("Users", "NIP");
                if (hasCompanyNip && !hasNip)
                {
                    Cmd("ALTER TABLE Users ADD COLUMN NIP TEXT;").ExecuteNonQuery();
                    Cmd("UPDATE Users SET NIP = CompanyNip WHERE NIP IS NULL AND CompanyNip IS NOT NULL;").ExecuteNonQuery();
                }

                if (!ColTx("Users", "Email")) Cmd("ALTER TABLE Users ADD COLUMN Email     TEXT NULL;").ExecuteNonQuery();
                if (!ColTx("Users", "FirstName")) Cmd("ALTER TABLE Users ADD COLUMN FirstName TEXT NULL;").ExecuteNonQuery();
                if (!ColTx("Users", "LastName")) Cmd("ALTER TABLE Users ADD COLUMN LastName  TEXT NULL;").ExecuteNonQuery();
                if (!ColTx("Users", "Address")) Cmd("ALTER TABLE Users ADD COLUMN Address   TEXT NULL;").ExecuteNonQuery();
                if (!ColTx("Users", "CreatedAt")) Cmd("ALTER TABLE Users ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;").ExecuteNonQuery();

                if (!ColTx("Categories", "UserId"))
                    Cmd("ALTER TABLE Categories ADD COLUMN UserId INTEGER NULL;").ExecuteNonQuery();

                using (var idx = Cmd(@"
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Username_NC
    ON Users(Username COLLATE NOCASE);

CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_User_Name
    ON Categories(COALESCE(UserId,0), Name);
"))
                {
                    idx.ExecuteNonQuery();
                }

                using (var check = Cmd("SELECT COUNT(1) FROM Categories;"))
                {
                    var cnt = Convert.ToInt32(check.ExecuteScalar());
                    if (cnt == 0)
                    {
                        using var seed = Cmd(@"
INSERT INTO Categories (Name) VALUES ('Jedzenie');
INSERT INTO Categories (Name) VALUES ('Transport');
INSERT INTO Categories (Name) VALUES ('Rachunki');");
                        seed.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
        }

        // --- PRAGMA z transakcją (istniejące) ---
        private static bool ColumnExists(SqliteConnection con, SqliteTransaction tx, string table, string column)
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA table_info('{table.Replace("'", "''")}');";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r["name"]?.ToString();
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // --- NOWE przeciążenie bez transakcji (dla prostych sprawdzeń) ---
        private static bool ColumnExists(SqliteConnection con, string table, string column)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{table.Replace("'", "''")}');";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r["name"]?.ToString();
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}








