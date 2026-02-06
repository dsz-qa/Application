using System;
using Microsoft.Data.Sqlite;

namespace Finly.Services.Features
{
    /// <summary>
    /// Jedyny właściciel schematu SQLite:
    /// - CREATE TABLE (idempotentnie)
    /// - migracje kolumn (ALTER TABLE ... ADD COLUMN)
    /// - przebudowy tabel, gdy wymagane (np. FK / NULLability)
    /// - indeksy
    ///
    /// WAŻNE (SQLite): ALTER TABLE ADD COLUMN nie pozwala na DEFAULT będący wyrażeniem
    /// (np. CURRENT_TIMESTAMP, date('now')). To powodowało błąd startu aplikacji.
    /// Ten plik robi teraz migracje takich kolumn w trybie:
    ///  - ADD COLUMN bez DEFAULT i bez NOT NULL
    ///  - backfill UPDATE dla istniejących rekordów
    ///  - (opcjonalna przebudowa tabeli to osobny temat; tu nie jest wymagana do stabilnego działania)
    /// </summary>
    public static class SchemaService
    {
        private static readonly object _schemaLock = new();

        public static void Ensure(SqliteConnection con)
        {
            if (con == null) throw new ArgumentNullException(nameof(con));

            lock (_schemaLock)
            {
                // PRAGMA przed transakcją
                using (var p = con.CreateCommand())
                {
                    p.CommandText = @"
PRAGMA foreign_keys = ON;
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
                // Definicje CREATE są “pełne”, żeby świeża baza nie wymagała migracji/backfilli.
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

    OverState     INTEGER NOT NULL DEFAULT 0,
    OverNotifiedAt TEXT NULL,

    CategoryId    INTEGER NULL,

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
    SortOrder    INTEGER NOT NULL DEFAULT 0,
    LastSync     TEXT,
    FOREIGN KEY(UserId)       REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(ConnectionId) REFERENCES BankConnections(Id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS AppSettings(
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NOT NULL,
    Key         TEXT    NOT NULL,
    Value       TEXT    NULL,
    UpdatedAt   TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Incomes(
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NOT NULL,
    Amount      REAL NOT NULL,
    Date        TEXT NOT NULL,
    Description TEXT NULL,
    Source      TEXT NULL,
    CategoryId  INTEGER NULL,
    IsPlanned   INTEGER NOT NULL DEFAULT 0,
    BudgetId    INTEGER NULL,

    PaymentKind  INTEGER NOT NULL DEFAULT 0,
    PaymentRefId INTEGER NULL,

    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Expenses(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    Date          TEXT    NOT NULL,
    Amount        REAL    NOT NULL,
    Title         TEXT    NULL,
    Description   TEXT    NULL,
    CategoryId    INTEGER NULL,
    AccountId     INTEGER NULL,
    BudgetId      INTEGER NULL,
    Note          TEXT    NULL,
    IsPlanned     INTEGER NOT NULL DEFAULT 0,

    PaymentKind   INTEGER NOT NULL DEFAULT 0,
    PaymentRefId  INTEGER NULL,

    LoanId            INTEGER NULL,
    LoanInstallmentId INTEGER NULL,

    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Transfers(
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId            INTEGER NOT NULL,
    Amount            NUMERIC NOT NULL,
    Date              TEXT NOT NULL,
    Description       TEXT NULL,

    -- nowy, docelowy zapis (spójny z Expenses/Incomes)
    FromPaymentKind   INTEGER NOT NULL DEFAULT 0,
    FromPaymentRefId  INTEGER NULL,
    ToPaymentKind     INTEGER NOT NULL DEFAULT 0,
    ToPaymentRefId    INTEGER NULL,

    -- legacy (zostawiamy dla wstecznej kompatybilności / istniejących danych)
    FromKind          TEXT NULL,
    FromRefId         INTEGER NULL,
    ToKind            TEXT NULL,
    ToRefId           INTEGER NULL,

    IsPlanned         INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS Loans(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId       INTEGER NOT NULL,
    Name         TEXT NOT NULL,
    Principal    NUMERIC NOT NULL DEFAULT 0,
    InterestRate NUMERIC NOT NULL DEFAULT 0,
    StartDate    TEXT NOT NULL DEFAULT (date('now')),
    TermMonths   INTEGER NOT NULL DEFAULT 0,
    PaymentDay   INTEGER NOT NULL DEFAULT 0,
    Note         TEXT NULL,

    SchedulePath TEXT NULL,

    PaymentKind  INTEGER NOT NULL DEFAULT 0,
    PaymentRefId INTEGER NULL,

    OverrideMonthlyPayment NUMERIC NULL,
    OverrideRemainingMonths INTEGER NULL,

    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS LoanSchedules(
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId       INTEGER NOT NULL,
    LoanId       INTEGER NOT NULL,
    ImportedAt   TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    SourceName   TEXT NULL,
    SchedulePath TEXT NULL,
    Note         TEXT NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(LoanId) REFERENCES Loans(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS LoanInstallments(
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId           INTEGER NOT NULL,
    LoanId           INTEGER NOT NULL,
    ScheduleId       INTEGER NOT NULL,
    InstallmentNo    INTEGER NOT NULL,
    DueDate          TEXT NOT NULL,
    TotalAmount      NUMERIC NOT NULL DEFAULT 0,

    PrincipalAmount  NUMERIC NULL,
    InterestAmount   NUMERIC NULL,
    RemainingBalance NUMERIC NULL,

    Status           INTEGER NOT NULL DEFAULT 0,
    PaidAt           TEXT NULL,

    PaymentKind      INTEGER NOT NULL DEFAULT 0,
    PaymentRefId     INTEGER NULL,

    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(LoanId) REFERENCES Loans(Id) ON DELETE CASCADE,
    FOREIGN KEY(ScheduleId) REFERENCES LoanSchedules(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS LoanOperations(
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId           INTEGER NOT NULL,
    LoanId           INTEGER NOT NULL,
    Date             TEXT NOT NULL,
    Type             INTEGER NOT NULL,

    TotalAmount      NUMERIC NOT NULL DEFAULT 0,
    CapitalPart      NUMERIC NOT NULL DEFAULT 0,
    InterestPart     NUMERIC NOT NULL DEFAULT 0,
    RemainingPrincipal NUMERIC NOT NULL DEFAULT 0,

    Note             TEXT NULL,

    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(LoanId) REFERENCES Loans(Id) ON DELETE CASCADE
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
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    Name          TEXT    NOT NULL,
    Target        NUMERIC NOT NULL DEFAULT 0,
    Allocated     NUMERIC NOT NULL DEFAULT 0,
    Note          TEXT    NULL,
    SortOrder     INTEGER NULL,
    GoalSortOrder INTEGER NULL,
    Deadline      TEXT NULL,
    GoalText      TEXT NULL,
    CreatedAt     TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
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
                // Uwaga: tu NIE WOLNO używać DEFAULT z wyrażeniem w ALTER TABLE ADD COLUMN.

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

                // Budgets
                AddColumnIfMissing(con, tx, "Budgets", "OverState", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Budgets", "OverNotifiedAt", "TEXT");
                AddColumnIfMissing(con, tx, "Budgets", "CategoryId", "INTEGER");

                // Categories
                AddColumnIfMissing(con, tx, "Categories", "Type", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Categories", "Color", "TEXT");
                AddColumnIfMissing(con, tx, "Categories", "Icon", "TEXT");
                AddColumnIfMissing(con, tx, "Categories", "Description", "TEXT");
                AddColumnIfMissing(con, tx, "Categories", "IsArchived", "INTEGER", "NOT NULL DEFAULT 0");

                // BankAccounts
                AddColumnIfMissing(con, tx, "BankAccounts", "BankName", "TEXT");
                AddColumnIfMissing(con, tx, "BankAccounts", "SortOrder", "INTEGER", "NOT NULL DEFAULT 0");

                // Incomes
                AddColumnIfMissing(con, tx, "Incomes", "Description", "TEXT");
                AddColumnIfMissing(con, tx, "Incomes", "Source", "TEXT");
                AddColumnIfMissing(con, tx, "Incomes", "CategoryId", "INTEGER");
                AddColumnIfMissing(con, tx, "Incomes", "IsPlanned", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Incomes", "BudgetId", "INTEGER");
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
                AddColumnIfMissing(con, tx, "Expenses", "LoanId", "INTEGER");
                AddColumnIfMissing(con, tx, "Expenses", "LoanInstallmentId", "INTEGER");

                // Transfers
                AddColumnIfMissing(con, tx, "Transfers", "FromPaymentKind", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Transfers", "FromPaymentRefId", "INTEGER");
                AddColumnIfMissing(con, tx, "Transfers", "ToPaymentKind", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Transfers", "ToPaymentRefId", "INTEGER");

                // Transfers (legacy - jeśli stara baza nie miała tych kolumn)
                AddColumnIfMissing(con, tx, "Transfers", "FromKind", "TEXT");
                AddColumnIfMissing(con, tx, "Transfers", "FromRefId", "INTEGER");
                AddColumnIfMissing(con, tx, "Transfers", "ToKind", "TEXT");
                AddColumnIfMissing(con, tx, "Transfers", "ToRefId", "INTEGER");


                // Loans (tu był killer: DEFAULT (date('now')))
                AddColumnIfMissing(con, tx, "Loans", "Principal", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "InterestRate", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "StartDate", "TEXT", "NOT NULL DEFAULT (date('now'))");
                AddColumnIfMissing(con, tx, "Loans", "TermMonths", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "PaymentDay", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "Note", "TEXT");
                AddColumnIfMissing(con, tx, "Loans", "SchedulePath", "TEXT");
                AddColumnIfMissing(con, tx, "Loans", "PaymentKind", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Loans", "PaymentRefId", "INTEGER");
                AddColumnIfMissing(con, tx, "Loans", "OverrideMonthlyPayment", "NUMERIC");
                AddColumnIfMissing(con, tx, "Loans", "OverrideRemainingMonths", "INTEGER");

                // Investments
                AddColumnIfMissing(con, tx, "Investments", "Type", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Investments", "TargetAmount", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Investments", "CurrentAmount", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "Investments", "TargetDate", "TEXT");
                AddColumnIfMissing(con, tx, "Investments", "Description", "TEXT");

                // InvestmentValuations (Date default też bywa problematyczny)
                AddColumnIfMissing(con, tx, "InvestmentValuations", "UserId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "InvestmentValuations", "InvestmentId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "InvestmentValuations", "Date", "TEXT", "NOT NULL DEFAULT (date('now'))");
                AddColumnIfMissing(con, tx, "InvestmentValuations", "Value", "NUMERIC", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "InvestmentValuations", "Note", "TEXT");

                // Envelopes
                AddColumnIfMissing(con, tx, "Envelopes", "Deadline", "TEXT");
                AddColumnIfMissing(con, tx, "Envelopes", "GoalText", "TEXT");
                AddColumnIfMissing(con, tx, "Envelopes", "GoalSortOrder", "INTEGER");
                AddColumnIfMissing(con, tx, "Envelopes", "SortOrder", "INTEGER");

                // LoanSchedules (ImportedAt default CURRENT_TIMESTAMP)
                AddColumnIfMissing(con, tx, "LoanSchedules", "UserId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanSchedules", "LoanId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanSchedules", "ImportedAt", "TEXT", "NOT NULL DEFAULT CURRENT_TIMESTAMP");
                AddColumnIfMissing(con, tx, "LoanSchedules", "SourceName", "TEXT");
                AddColumnIfMissing(con, tx, "LoanSchedules", "SchedulePath", "TEXT");
                AddColumnIfMissing(con, tx, "LoanSchedules", "Note", "TEXT");

                // LoanInstallments (DueDate default date('now') itp.)
                AddColumnIfMissing(con, tx, "LoanInstallments", "UserId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanInstallments", "LoanId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanInstallments", "ScheduleId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanInstallments", "InstallmentNo", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanInstallments", "DueDate", "TEXT", "NOT NULL DEFAULT (date('now'))");
                AddColumnIfMissing(con, tx, "LoanInstallments", "TotalAmount", "NUMERIC", "NOT NULL DEFAULT 0");

                AddColumnIfMissing(con, tx, "LoanInstallments", "PrincipalAmount", "NUMERIC");
                AddColumnIfMissing(con, tx, "LoanInstallments", "InterestAmount", "NUMERIC");
                AddColumnIfMissing(con, tx, "LoanInstallments", "RemainingBalance", "NUMERIC");

                AddColumnIfMissing(con, tx, "LoanInstallments", "Status", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanInstallments", "PaidAt", "TEXT");

                AddColumnIfMissing(con, tx, "LoanInstallments", "PaymentKind", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "LoanInstallments", "PaymentRefId", "INTEGER");

                // AppSettings (stare bazy mogły mieć inną wersję)
                AddColumnIfMissing(con, tx, "AppSettings", "UserId", "INTEGER", "NOT NULL DEFAULT 0");
                AddColumnIfMissing(con, tx, "AppSettings", "Key", "TEXT", "NOT NULL DEFAULT ''");
                AddColumnIfMissing(con, tx, "AppSettings", "Value", "TEXT");
                AddColumnIfMissing(con, tx, "AppSettings", "UpdatedAt", "TEXT", "NOT NULL DEFAULT CURRENT_TIMESTAMP");

                // Backfill: jeśli ktoś miał Description a Title puste
                if (ColumnExists(con, tx, "Expenses", "Title") && ColumnExists(con, tx, "Expenses", "Description"))
                {
                    using var backfill = Cmd(@"
UPDATE Expenses
SET Title = COALESCE(Title, Description)
WHERE Title IS NULL AND Description IS NOT NULL;");
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

CREATE INDEX IF NOT EXISTS IX_Expenses_User_Loan
    ON Expenses(UserId, LoanId);

CREATE UNIQUE INDEX IF NOT EXISTS IX_Expenses_UserId_LoanInstallmentId
    ON Expenses(UserId, LoanInstallmentId)
    WHERE LoanInstallmentId IS NOT NULL;

CREATE INDEX IF NOT EXISTS IX_Expenses_UserId_LoanId_Date
    ON Expenses(UserId, LoanId, Date);

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

CREATE INDEX IF NOT EXISTS IX_LoanSchedules_User_Loan
    ON LoanSchedules(UserId, LoanId, ImportedAt);

CREATE UNIQUE INDEX IF NOT EXISTS UX_LoanInstallments_Loan_No
    ON LoanInstallments(UserId, LoanId, InstallmentNo);

CREATE INDEX IF NOT EXISTS IX_LoanInstallments_Loan_DueDate
    ON LoanInstallments(UserId, LoanId, DueDate);

CREATE INDEX IF NOT EXISTS IX_LoanInstallments_Status_DueDate
    ON LoanInstallments(UserId, Status, DueDate);

CREATE INDEX IF NOT EXISTS IX_LoanInstallments_Schedule
    ON LoanInstallments(UserId, ScheduleId);

CREATE INDEX IF NOT EXISTS IX_LoanOperations_User_Loan_Date
    ON LoanOperations(UserId, LoanId, Date);

CREATE INDEX IF NOT EXISTS IX_LoanOperations_User_Loan_Type
    ON LoanOperations(UserId, LoanId, Type);


CREATE UNIQUE INDEX IF NOT EXISTS UX_Expenses_PlannedLoanInstallment
ON Expenses(UserId, LoanInstallmentId)
WHERE IsPlanned=1 AND LoanInstallmentId IS NOT NULL;

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

CREATE INDEX IF NOT EXISTS IX_Budgets_User_Category
    ON Budgets(UserId, CategoryId);

CREATE UNIQUE INDEX IF NOT EXISTS UX_AppSettings_User_Key
    ON AppSettings(UserId, Key COLLATE NOCASE);
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

        private static bool HasNonConstantDefault(string extra)
        {
            if (string.IsNullOrWhiteSpace(extra)) return false;

            // W SQLite "non-constant default" przy ALTER TABLE to m.in.:
            // - CURRENT_TIMESTAMP / CURRENT_DATE / CURRENT_TIME
            // - date('now'), datetime('now'), julianday('now') ...
            // - jakiekolwiek DEFAULT (...)
            var e = extra.ToUpperInvariant();

            if (!e.Contains("DEFAULT")) return false;

            if (e.Contains("DEFAULT (")) return true;

            return e.Contains("CURRENT_TIMESTAMP")
                || e.Contains("CURRENT_DATE")
                || e.Contains("CURRENT_TIME")
                || e.Contains("DATE('NOW'")
                || e.Contains("DATETIME('NOW'")
                || e.Contains("JULIANDAY('NOW'");
        }

        private static string? InferBackfillExpression(string extra)
        {
            if (string.IsNullOrWhiteSpace(extra)) return null;
            var e = extra.ToUpperInvariant();

            if (e.Contains("DATE('NOW'")) return "date('now')";
            if (e.Contains("DATETIME('NOW'")) return "datetime('now')";
            if (e.Contains("JULIANDAY('NOW'")) return "julianday('now')";

            if (e.Contains("CURRENT_DATE")) return "CURRENT_DATE";
            if (e.Contains("CURRENT_TIME")) return "CURRENT_TIME";

            // default fallback dla CreatedAt/UpdatedAt
            if (e.Contains("CURRENT_TIMESTAMP")) return "CURRENT_TIMESTAMP";

            // jeśli było DEFAULT (...) ale nie rozpoznaliśmy — nic nie backfilluj
            return null;
        }

        private static void AddColumnIfMissing(
            SqliteConnection con,
            SqliteTransaction tx,
            string table,
            string column,
            string sqlType,
            string extra = "")
        {
            if (string.IsNullOrWhiteSpace(table)) throw new ArgumentException("Table name is required.", nameof(table));
            if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("Column name is required.", nameof(column));
            if (string.IsNullOrWhiteSpace(sqlType)) throw new ArgumentException("SQL type is required.", nameof(sqlType));

            if (ColumnExists(con, tx, table, column)) return;

            bool nonConstDefault = HasNonConstantDefault(extra);

            using var alter = con.CreateCommand();
            alter.Transaction = tx;

            if (!nonConstDefault)
            {
                // Bezpieczny przypadek: DEFAULT jest stałą (np. 0, 'x') albo brak DEFAULT
                alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType} {extra};";
                alter.ExecuteNonQuery();
                return;
            }

            // Problematiczny przypadek (SQLite): DEFAULT to wyrażenie / funkcja.
            // Rozwiązanie:
            // 1) ADD COLUMN bez DEFAULT i bez NOT NULL
            // 2) backfill istniejących rekordów (jeśli wiemy jak)
            // 3) zostawiamy kolumnę nullable (zmiana constraints wymaga rebuild tabeli)
            alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType};";
            alter.ExecuteNonQuery();

            var backfillExpr = InferBackfillExpression(extra);
            if (!string.IsNullOrWhiteSpace(backfillExpr))
            {
                using var upd = con.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = $@"
UPDATE ""{table}""
SET ""{column}"" = {backfillExpr}
WHERE ""{column}"" IS NULL;";
                upd.ExecuteNonQuery();
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
            if (c == null) throw new ArgumentNullException(nameof(c));

            // Jeśli ConnectionId już jest NULLABLE – OK (albo tabela/kolumna nie istnieje -> nic nie robimy)
            // Uwaga: ColumnIsNotNull zwróci false, gdy nie znajdzie kolumny – i wtedy też wyjdziemy.
            if (!ColumnIsNotNull(c, "BankAccounts", "ConnectionId"))
                return;

            using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;

            // SQLite: przebudowy tabel z FK najbezpieczniej robić z foreign_keys=OFF w tej samej transakcji
            cmd.CommandText = "PRAGMA foreign_keys=OFF;";
            cmd.ExecuteNonQuery();

            // (Opcjonalnie) WAL/busy_timeout masz ustawione wcześniej w Ensure(), więc tu nie musisz.

            // 1) Tworzymy nową tabelę z docelowym ConnectionId NULL + FK ON DELETE SET NULL
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS BankAccounts_new(
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

            // 2) Przenosimy dane (COALESCE dla SortOrder – gdyby stare rekordy miały NULL)
            cmd.CommandText = @"
INSERT INTO BankAccounts_new (Id, ConnectionId, UserId, BankName, AccountName, Iban, Currency, Balance, SortOrder, LastSync)
SELECT Id,
       ConnectionId,
       UserId,
       BankName,
       AccountName,
       Iban,
       Currency,
       Balance,
       COALESCE(SortOrder, 0),
       LastSync
FROM BankAccounts;";
            cmd.ExecuteNonQuery();

            // 3) Podmieniamy tabelę
            cmd.CommandText = "DROP TABLE BankAccounts;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "ALTER TABLE BankAccounts_new RENAME TO BankAccounts;";
            cmd.ExecuteNonQuery();

            // 4) Włączamy FK z powrotem
            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();

            // (Opcjonalnie, ale polecam w dev) szybka walidacja spójności FK po rebuildzie
            // Jeśli chcesz: odkomentuj i ewentualnie loguj wyniki.
            // cmd.CommandText = "PRAGMA foreign_key_check;";
            // using var r = cmd.ExecuteReader();
            // if (r.Read())
            //     throw new InvalidOperationException("FK check failed after BankAccounts rebuild.");

            tx.Commit();
        }

    }
}
