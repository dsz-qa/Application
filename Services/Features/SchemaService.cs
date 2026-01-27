using System;
using Microsoft.Data.Sqlite;

namespace Finly.Services.Features
{
    /// <summary>
    /// Jedyny właściciel schematu SQLite:
    /// - CREATE TABLE (idempotentnie)
    /// - migracje kolumn (ALTER TABLE ... ADD COLUMN)
    /// - przebudowy tabel, gdy wymagane (np. FK)
    /// - indeksy
    /// </summary>
    public static class SchemaService
    {
        private static readonly object _schemaLock = new();

        public static void Ensure(SqliteConnection con)
        {
            if (con == null) throw new ArgumentNullException(nameof(con));

            lock (_schemaLock)
            {
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

                // ===== 1) CREATE TABLE (idempotentnie) =====
                using (var cmd = Cmd(@"
CREATE TABLE IF NOT EXISTS Users(
    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    Username                TEXT NOT NULL UNIQUE,
    PasswordHash            TEXT NOT NULL,
    Email                   TEXT NULL,
    FirstName               TEXT NULL,
    LastName                TEXT NULL,
    Address                 TEXT NULL,
    AccountType             TEXT NULL,
    CompanyName             TEXT NULL,
    NIP                     TEXT NULL,
    REGON                   TEXT NULL,
    KRS                     TEXT NULL,
    CompanyAddress          TEXT NULL,
    CreatedAt               TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    IsOnboarded             INTEGER NOT NULL DEFAULT 0,
    HasSeenEnvelopesIntro   INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Budgets (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    Name          TEXT    NOT NULL,
    Type          TEXT    NOT NULL,
    StartDate     TEXT    NOT NULL,
    EndDate       TEXT    NOT NULL,
    PlannedAmount REAL    NOT NULL,
    IsDeleted     INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Categories(
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NOT NULL,
    Name        TEXT NOT NULL,
    Type        INTEGER NOT NULL DEFAULT 0,
    Color       TEXT NULL,
    Icon        TEXT NULL,
    Description TEXT NULL,
    IsArchived  INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS BankConnections(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    BankName      TEXT NOT NULL,
    AccountHolder TEXT NOT NULL,
    Status        TEXT NOT NULL,
    LastSync      TEXT,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS BankAccounts(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ConnectionId INTEGER NULL,
    UserId       INTEGER NOT NULL,
    BankName     TEXT NULL,
    AccountName  TEXT NOT NULL,
    Iban         TEXT NOT NULL,
    Currency     TEXT NOT NULL,
    Balance      NUMERIC NOT NULL DEFAULT 0,
    LastSync     TEXT,
    FOREIGN KEY(UserId)       REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(ConnectionId) REFERENCES BankConnections(Id) ON DELETE SET NULL
);

-- Incomes: planned + stabilne księgowanie (PaymentKind/PaymentRefId)
CREATE TABLE IF NOT EXISTS Incomes(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Amount REAL NOT NULL,
    Date TEXT NOT NULL,
    Description TEXT NULL,
    Source TEXT NULL,
    CategoryId INTEGER NULL,
    IsPlanned INTEGER NOT NULL DEFAULT 0,
    BudgetId INTEGER NULL,
    PaymentKind  INTEGER NOT NULL DEFAULT 0,
    PaymentRefId INTEGER NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- Expenses: planned + stabilne księgowanie (PaymentKind/PaymentRefId)
CREATE TABLE IF NOT EXISTS Expenses(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId       INTEGER NOT NULL,
    Date         TEXT    NOT NULL,
    Amount       REAL    NOT NULL,
    Title        TEXT    NULL,
    Description  TEXT    NULL,
    CategoryId   INTEGER NULL,
    AccountId    INTEGER NULL,
    BudgetId     INTEGER NULL,
    Note         TEXT    NULL,
    IsPlanned    INTEGER NOT NULL DEFAULT 0,
    PaymentKind  INTEGER NOT NULL DEFAULT 0,
    PaymentRefId INTEGER NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Transfers(
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId     INTEGER NOT NULL,
    Amount     NUMERIC NOT NULL,
    Date       TEXT NOT NULL,
    Description TEXT NULL,
    FromKind   TEXT NOT NULL,
    FromRefId  INTEGER NULL,
    ToKind     TEXT NOT NULL,
    ToRefId    INTEGER NULL,
    IsPlanned  INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Loans(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId       INTEGER NOT NULL,
    Name         TEXT NOT NULL,
    Principal    NUMERIC NOT NULL DEFAULT 0,
    InterestRate NUMERIC NOT NULL DEFAULT 0,
    StartDate    TEXT NOT NULL,
    TermMonths   INTEGER NOT NULL DEFAULT 0,
    PaymentDay   INTEGER NOT NULL DEFAULT 0,
    Note         TEXT NULL,
    SchedulePath TEXT NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS Investments(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    Name          TEXT    NOT NULL,
    Type          INTEGER NOT NULL DEFAULT 0,
    TargetAmount  NUMERIC NOT NULL DEFAULT 0,
    CurrentAmount NUMERIC NOT NULL DEFAULT 0,
    TargetDate    TEXT    NULL,
    Description   TEXT    NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- BRAKOWAŁO: wyceny inwestycji (używane w DatabaseService)
CREATE TABLE IF NOT EXISTS InvestmentValuations(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId       INTEGER NOT NULL,
    InvestmentId INTEGER NOT NULL,
    Date         TEXT    NOT NULL,
    Value        NUMERIC NOT NULL DEFAULT 0,
    Note         TEXT    NULL,
    FOREIGN KEY(UserId)       REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(InvestmentId) REFERENCES Investments(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Envelopes(
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId     INTEGER NOT NULL,
    Name       TEXT    NOT NULL,
    Target     NUMERIC NOT NULL DEFAULT 0,
    Allocated  NUMERIC NOT NULL DEFAULT 0,
    Note       TEXT    NULL,
    SortOrder INTEGER NULL,
    GoalSortOrder INTEGER NULL,
    CreatedAt  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS CashOnHand(
    UserId     INTEGER PRIMARY KEY,
    Amount     NUMERIC NOT NULL DEFAULT 0,
    UpdatedAt  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS SavedCash(
    UserId     INTEGER PRIMARY KEY,
    Amount     NUMERIC NOT NULL DEFAULT 0,
    UpdatedAt  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
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

                // ===== 2) Migracje kolumn (idempotentne) =====

                // Users
                AddColumnIfMissing(con, tx, "Users", "Email", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "FirstName", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "LastName", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "Address", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "AccountType", "TEXT", "DEFAULT 'Personal'");
                AddColumnIfMissing(con, tx, "Users", "CompanyName", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "NIP", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "REGON", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "KRS", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "CompanyAddress", "TEXT");
                AddColumnIfMissing(con, tx, "Users", "CreatedAt", "TEXT", "NOT NULL DEFAULT CURRENT_TIMESTAMP");
                AddColumnIfMissing(con, tx, "Users", "IsOnboarded", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Users", "HasSeenEnvelopesIntro", "INTEGER", "NOT NULL DEFAULT 0");

                // Budgets (UI: alerty/over)
                AddColumnIfMissing(con, tx, "Budgets", "OverState", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Budgets", "OverNotifiedAt", "TEXT");

                // NOWE (wariant B): Budgets -> Categories
                AddColumnIfMissing(con, tx, "Budgets", "CategoryId", "INTEGER");

                // Categories
                AddColumnIfMissing(con, tx, "Categories", "Type", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Categories", "Color", "TEXT");
                AddColumnIfMissing(con, tx, "Categories", "Icon", "TEXT");
                AddColumnIfMissing(con, tx, "Categories", "Description", "TEXT");
                AddColumnIfMissing(con, tx, "Categories", "IsArchived", "INTEGER", "NOT NULL DEFAULT 0");

                // BankAccounts (UI używa BankName)
                // BankAccounts (UI używa BankName)
                AddColumnIfMissing(con, tx, "BankAccounts", "BankName", "TEXT");

                // NOWE: SortOrder do ręcznego sortowania kafelków kont
                AddColumnIfMissing(con, tx, "BankAccounts", "SortOrder", "INTEGER", "NOT NULL DEFAULT 0");


                // Incomes
                AddColumnIfMissing(con, tx, "Incomes", "Description", "TEXT");
                AddColumnIfMissing(con, tx, "Incomes", "Source", "TEXT");
                AddColumnIfMissing(con, tx, "Incomes", "CategoryId", "INTEGER");
                AddColumnIfMissing(con, tx, "Incomes", "IsPlanned", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Incomes", "BudgetId", "INTEGER");

                // NOWE: stabilne księgowanie przychodów
                AddColumnIfMissing(con, tx, "Incomes", "PaymentKind", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Incomes", "PaymentRefId", "INTEGER");

                // Expenses
                AddColumnIfMissing(con, tx, "Expenses", "Title", "TEXT");
                AddColumnIfMissing(con, tx, "Expenses", "Description", "TEXT");
                AddColumnIfMissing(con, tx, "Expenses", "CategoryId", "INTEGER");
                AddColumnIfMissing(con, tx, "Expenses", "AccountId", "INTEGER");
                AddColumnIfMissing(con, tx, "Expenses", "BudgetId", "INTEGER");
                AddColumnIfMissing(con, tx, "Expenses", "Note", "TEXT");
                AddColumnIfMissing(con, tx, "Expenses", "IsPlanned", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Expenses", "PaymentKind", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Expenses", "PaymentRefId", "INTEGER");

                // Loans
                AddColumnIfMissing(con, tx, "Loans", "Principal", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "InterestRate", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "StartDate", "TEXT", "NOT NULL DEFAULT (date('now'))");
                AddColumnIfMissing(con, tx, "Loans", "TermMonths", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "PaymentDay", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "Note", "TEXT");
                AddColumnIfMissing(con, tx, "Loans", "SchedulePath", "TEXT");


                // Investments
                AddColumnIfMissing(con, tx, "Investments", "Type", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Investments", "TargetAmount", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Investments", "CurrentAmount", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Investments", "TargetDate", "TEXT");
                AddColumnIfMissing(con, tx, "Investments", "Description", "TEXT");

                // Envelopes – brakujące kolumny używane w kodzie
                AddColumnIfMissing(con, tx, "Envelopes", "Deadline", "TEXT");
                AddColumnIfMissing(con, tx, "Envelopes", "GoalText", "TEXT");
                AddColumnIfMissing(con, tx, "Envelopes", "GoalSortOrder", "INTEGER");
                AddColumnIfMissing(con, tx, "Envelopes", "SortOrder", "INTEGER");


                // Backfill: jeśli ktoś miał Description a Title puste
                if (ColumnExists(con, tx, "Expenses", "Title") && ColumnExists(con, tx, "Expenses", "Description"))
                {
                    using var backfill = Cmd(
                        "UPDATE Expenses SET Title = COALESCE(Title, Description) " +
                        "WHERE Title IS NULL AND Description IS NOT NULL;");
                    backfill.ExecuteNonQuery();
                }

                tx.Commit();

                // ===== 3) Migracje wymagające przebudowy tabel (po tx) =====
                Ensure_BankAccounts_ConnectionId_Nullable_And_FK(con);

                // ===== 4) Indeksy =====
                using var idx = con.CreateCommand();
                idx.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Username_NC
    ON Users(Username COLLATE NOCASE);

CREATE INDEX IF NOT EXISTS IX_Categories_User_Name
    ON Categories(UserId, Name COLLATE NOCASE);

CREATE INDEX IF NOT EXISTS IX_Categories_User_Type
    ON Categories(UserId, Type, IsArchived);

CREATE INDEX IF NOT EXISTS IX_Expenses_User_Date
    ON Expenses(UserId, Date);

CREATE INDEX IF NOT EXISTS IX_Expenses_User_Category
    ON Expenses(UserId, CategoryId);

CREATE INDEX IF NOT EXISTS IX_Expenses_User_Account
    ON Expenses(UserId, AccountId);

CREATE INDEX IF NOT EXISTS IX_Expenses_User_PaymentKind
    ON Expenses(UserId, PaymentKind);

CREATE INDEX IF NOT EXISTS IX_Incomes_User_Date
    ON Incomes(UserId, Date);

CREATE INDEX IF NOT EXISTS IX_Incomes_User_PaymentKind
    ON Incomes(UserId, PaymentKind);

CREATE INDEX IF NOT EXISTS IX_Envelopes_User
    ON Envelopes(UserId);

CREATE INDEX IF NOT EXISTS IX_Envelopes_User_GoalSortOrder
    ON Envelopes(UserId, GoalSortOrder);

CREATE INDEX IF NOT EXISTS IX_Envelopes_User_SortOrder
    ON Envelopes(UserId, SortOrder);

CREATE INDEX IF NOT EXISTS IX_Investments_User
    ON Investments(UserId);

CREATE INDEX IF NOT EXISTS IX_InvestmentValuations_User_Inv_Date
    ON InvestmentValuations(UserId, InvestmentId, Date);

CREATE INDEX IF NOT EXISTS IX_InvestmentValuations_User_Date
    ON InvestmentValuations(UserId, Date);

CREATE INDEX IF NOT EXISTS IX_BankAccounts_User_SortOrder
    ON BankAccounts(UserId, SortOrder);

CREATE INDEX IF NOT EXISTS IX_BankConnections_User
    ON BankConnections(UserId);

-- NOWE: budżety po kategorii (wariant B)
CREATE INDEX IF NOT EXISTS IX_Budgets_User_Category
    ON Budgets(UserId, CategoryId);
";
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

        private static void AddColumnIfMissing(
            SqliteConnection con,
            SqliteTransaction tx,
            string table,
            string column,
            string sqlType,
            string extra = "")
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
            // Jeśli ConnectionId już jest NULLABLE – OK
            if (!ColumnIsNotNull(c, "BankAccounts", "ConnectionId")) return;

            using var t = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;

            cmd.CommandText = "PRAGMA foreign_keys=OFF;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
CREATE TABLE BankAccounts_new(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ConnectionId INTEGER NULL,
    UserId       INTEGER NOT NULL,
    BankName     TEXT NULL,
    AccountName  TEXT NOT NULL,
    Iban         TEXT NOT NULL,
    Currency     TEXT NOT NULL,
    Balance      NUMERIC NOT NULL DEFAULT 0,
    SortOrder    INTEGER NOT NULL DEFAULT 0,
    LastSync     TEXT,
    FOREIGN KEY(UserId)       REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(ConnectionId) REFERENCES BankConnections(Id) ON DELETE SET NULL
);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
INSERT INTO BankAccounts_new (Id, ConnectionId, UserId, BankName, AccountName, Iban, Currency, Balance, SortOrder, LastSync)
SELECT Id, ConnectionId, UserId,
       BankName,
       AccountName, Iban, Currency, Balance,
       COALESCE(SortOrder, 0),
       LastSync
FROM BankAccounts;";
            cmd.ExecuteNonQuery();


            cmd.CommandText = "DROP TABLE BankAccounts;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "ALTER TABLE BankAccounts_new RENAME TO BankAccounts;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();

            t.Commit();
        }
    }
}
