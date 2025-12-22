using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.Linq;

namespace Finly.Services.Features
{
    /// <summary>
    /// Jedyny właściciel księgowania w aplikacji.
    /// Tu jest cała logika wpływu na salda (CashOnHand / SavedCash / Envelopes / BankAccounts)
    /// oraz odwracanie transakcji przy usuwaniu.
    /// </summary>
    public static class LedgerService
    {
        // ====== KONWENCJE PaymentKind (STABILNE KSIĘGOWANIE) ======
        // Jeśli masz enum w Models, docelowo warto ujednolicić i nie dublować.
        private enum PaymentKind : int
        {
            FreeCash = 0,
            SavedCash = 1,
            Envelope = 2,
            BankAccount = 3
        }

        private static string ToIsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static void EnsureNonNegative(decimal amount, string paramName)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi być dodatnia.", paramName);
        }

        // =========================
        //  (0) Transfers – baza
        // =========================

        // Normalizujemy string kindów, bo w DB trzymasz FromKind/ToKind jako TEXT
        private static string NormKind(string? s) => (s ?? "").Trim().ToLowerInvariant();

        // Stałe kindy – spójne dla całej aplikacji (DB + UI)
        private const string KIND_FREE = "freecash";
        private const string KIND_SAVED = "savedcash";
        private const string KIND_ENV = "envelope";
        private const string KIND_BANK = "bank";
        private const string KIND_LEGACY_CASH = "cash"; // legacy fallback

        private static void EnsureTransfersTable(SqliteConnection c, SqliteTransaction tx)
        {
            // Jeśli DatabaseService.EnsureTables() już to robi – to nie zaszkodzi.
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Transfers(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId    INTEGER NOT NULL,
    Amount    NUMERIC NOT NULL,
    Date      TEXT NOT NULL,
    Description TEXT NULL,
    FromKind  TEXT NOT NULL,
    FromRefId INTEGER NULL,
    ToKind    TEXT NOT NULL,
    ToRefId   INTEGER NULL,
    IsPlanned INTEGER NOT NULL DEFAULT 0
);";
            cmd.ExecuteNonQuery();
        }

        private static int InsertTransferRow(
            SqliteConnection c, SqliteTransaction tx,
            int userId, decimal amount, DateTime date, string? description,
            string fromKind, int? fromRefId,
            string toKind, int? toRefId,
            bool isPlanned)
        {
            EnsureTransfersTable(c, tx);

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO Transfers(UserId, Amount, Date, Description, FromKind, FromRefId, ToKind, ToRefId, IsPlanned)
VALUES(@u,@a,@d,@desc,@fk,@fr,@tk,@tr,@p);
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@d", ToIsoDate(date));
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@fk", fromKind);
            cmd.Parameters.AddWithValue("@fr", (object?)fromRefId ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@tk", toKind);
            cmd.Parameters.AddWithValue("@tr", (object?)toRefId ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@p", isPlanned ? 1 : 0);

            var obj = cmd.ExecuteScalar();
            return Convert.ToInt32(obj);
        }

        /// <summary>
        /// Jedyny punkt wykonywania transferu (zapis + księgowanie) w 1 transakcji.
        /// Wspiera wszystkie kombinacje: free/saved/envelope/bank.
        /// </summary>
        public static int TransferAny(
            int userId,
            decimal amount,
            DateTime date,
            string? description,
            string fromKind, int? fromRefId,
            string toKind, int? toRefId,
            bool isPlanned = false)
        {
            EnsureNonNegative(amount, nameof(amount));

            fromKind = NormKind(fromKind);
            toKind = NormKind(toKind);

            if (fromKind == toKind && (fromRefId ?? 0) == (toRefId ?? 0))
                throw new InvalidOperationException("Źródło i cel transferu muszą być różne.");

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();

            using var tx = c.BeginTransaction();

            // 1) Zapis rekordu transferu (zawsze – nawet planned)
            var transferId = InsertTransferRow(
                c, tx,
                userId, amount, date, description,
                fromKind, fromRefId,
                toKind, toRefId,
                isPlanned);

            // 2) Księgowanie tylko jeśli zrealizowany
            if (!isPlanned)
            {
                ApplyTransferEffect(c, tx, userId, fromKind, fromRefId, toKind, toRefId, amount);
            }

            tx.Commit();
            DatabaseService.NotifyDataChanged();
            return transferId;
        }

        // =========================================
        //  (1) GET/SET gotówka (operacje niskopoziomowe)
        // =========================================

        private static decimal GetCashOnHand(SqliteConnection c, SqliteTransaction tx, int userId)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COALESCE(Amount,0) FROM CashOnHand WHERE UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        private static void AddCash(SqliteConnection c, SqliteTransaction tx, int userId, decimal amount)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO CashOnHand(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE
SET Amount = CashOnHand.Amount + excluded.Amount,
    UpdatedAt = CURRENT_TIMESTAMP;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();
        }

        private static void SubCash(SqliteConnection c, SqliteTransaction tx, int userId, decimal amount)
        {
            var current = GetCashOnHand(c, tx, userId);
            if (current < amount) throw new InvalidOperationException("Za mało gotówki w portfelu.");
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE CashOnHand SET Amount = Amount - @a, UpdatedAt=CURRENT_TIMESTAMP WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();
        }

        private static decimal GetSavedCash(SqliteConnection c, SqliteTransaction tx, int userId)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COALESCE(Amount,0) FROM SavedCash WHERE UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }

        private static void AddSaved(SqliteConnection c, SqliteTransaction tx, int userId, decimal amount)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO SavedCash(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE
SET Amount = SavedCash.Amount + excluded.Amount,
    UpdatedAt = CURRENT_TIMESTAMP;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();
        }

        private static void SubSaved(SqliteConnection c, SqliteTransaction tx, int userId, decimal amount)
        {
            var current = GetSavedCash(c, tx, userId);
            if (current < amount) throw new InvalidOperationException("Za mało odłożonej gotówki.");
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE SavedCash SET Amount = Amount - @a, UpdatedAt=CURRENT_TIMESTAMP WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();
        }

        private static void AddBank(SqliteConnection c, SqliteTransaction tx, int userId, int accountId, decimal amount)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE BankAccounts SET Balance = Balance + @a WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@id", accountId);
            cmd.Parameters.AddWithValue("@u", userId);
            var rows = cmd.ExecuteNonQuery();
            if (rows == 0) throw new InvalidOperationException("Nie znaleziono rachunku bankowego lub nie należy do użytkownika.");
        }

        private static void SubBank(SqliteConnection c, SqliteTransaction tx, int userId, int accountId, decimal amount)
        {
            decimal current;
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = @"SELECT COALESCE(Balance,0) FROM BankAccounts WHERE Id=@id AND UserId=@u LIMIT 1;";
                q.Parameters.AddWithValue("@id", accountId);
                q.Parameters.AddWithValue("@u", userId);
                var obj = q.ExecuteScalar();
                if (obj == null || obj == DBNull.Value) throw new InvalidOperationException("Nie znaleziono rachunku bankowego lub nie należy do użytkownika.");
                current = Convert.ToDecimal(obj);
            }

            if (current < amount) throw new InvalidOperationException("Na koncie bankowym brakuje środków.");

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE BankAccounts SET Balance = Balance - @a WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@id", accountId);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }

        private static decimal GetEnvelopeAllocated(SqliteConnection c, SqliteTransaction tx, int userId, int envelopeId)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT COALESCE(Allocated,0) FROM Envelopes WHERE Id=@id AND UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", envelopeId);
            cmd.Parameters.AddWithValue("@u", userId);
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) throw new InvalidOperationException("Nie znaleziono koperty.");
            return Convert.ToDecimal(obj);
        }

        private static void AddEnvelopeAllocated(SqliteConnection c, SqliteTransaction tx, int userId, int envelopeId, decimal amount)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE Envelopes SET Allocated = COALESCE(Allocated,0) + @a WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@id", envelopeId);
            cmd.Parameters.AddWithValue("@u", userId);
            var rows = cmd.ExecuteNonQuery();
            if (rows == 0) throw new InvalidOperationException("Nie znaleziono koperty.");
        }

        private static void SubEnvelopeAllocated(SqliteConnection c, SqliteTransaction tx, int userId, int envelopeId, decimal amount)
        {
            var current = GetEnvelopeAllocated(c, tx, userId, envelopeId);
            if (current < amount) throw new InvalidOperationException("W kopercie jest za mało środków.");
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE Envelopes SET Allocated = COALESCE(Allocated,0) - @a WHERE Id=@id AND UserId=@u;";
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.AddWithValue("@id", envelopeId);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        //  TRANSFER EFFECT – RDZEŃ (WSZYSTKIE KOMBINACJE)
        // ============================================================

        private static void ApplyTransferEffect(
            SqliteConnection c, SqliteTransaction tx,
            int userId,
            string fromKind, int? fromRefId,
            string toKind, int? toRefId,
            decimal amount)
        {
            fromKind = NormKind(fromKind);
            toKind = NormKind(toKind);

            // identyczne źródło/cel -> nic nie rób
            if (fromKind == toKind && fromRefId == toRefId) return;

            // ---- FROM side ----
            switch (fromKind)
            {
                case KIND_FREE:
                    // freecash = CashOnHand - SavedCash => realnie zdejmujemy z CashOnHand
                    {
                        var allCash = GetCashOnHand(c, tx, userId);
                        var saved = GetSavedCash(c, tx, userId);
                        var free = Math.Max(0m, allCash - saved);
                        if (free < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");
                        SubCash(c, tx, userId, amount);
                        break;
                    }

                case KIND_SAVED:
                    // savedcash to subset CashOnHand => zdejmujemy z SavedCash (CashOnHand bez zmian)
                    SubSaved(c, tx, userId, amount);
                    break;

                case KIND_ENV:
                    if (!fromRefId.HasValue) throw new InvalidOperationException("Brak FromRefId dla envelope.");
                    SubEnvelopeAllocated(c, tx, userId, fromRefId.Value, amount);
                    break;

                case KIND_BANK:
                    if (!fromRefId.HasValue) throw new InvalidOperationException("Brak FromRefId dla bank.");
                    SubBank(c, tx, userId, fromRefId.Value, amount);
                    break;

                case KIND_LEGACY_CASH:
                    SubCash(c, tx, userId, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieznany FromKind: '{fromKind}'.");
            }

            // ---- TO side ----
            switch (toKind)
            {
                case KIND_FREE:
                    AddCash(c, tx, userId, amount);
                    break;

                case KIND_SAVED:
                    AddSaved(c, tx, userId, amount);
                    break;

                case KIND_ENV:
                    if (!toRefId.HasValue) throw new InvalidOperationException("Brak ToRefId dla envelope.");
                    AddEnvelopeAllocated(c, tx, userId, toRefId.Value, amount);
                    break;

                case KIND_BANK:
                    if (!toRefId.HasValue) throw new InvalidOperationException("Brak ToRefId dla bank.");
                    AddBank(c, tx, userId, toRefId.Value, amount);
                    break;

                case KIND_LEGACY_CASH:
                    AddCash(c, tx, userId, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieznany ToKind: '{toKind}'.");
            }
        }

        // =========================
        //  EXPENSE EFFECT (zgodne z DatabaseService)
        //  PaymentKind przychodzi jako int (Finly.Models.PaymentKind)
        // =========================
        public static void ApplyExpenseEffect(
            SqliteConnection c,
            SqliteTransaction tx,
            int userId,
            decimal amount,
            int paymentKind,
            int? paymentRefId)
        {
            EnsureNonNegative(amount, nameof(amount));

            switch (paymentKind)
            {
                // FreeCash
                case 0:
                    {
                        // wolna gotówka = CashOnHand - SavedCash, ale zdejmujemy z CashOnHand
                        var allCash = GetCashOnHand(c, tx, userId);
                        var saved = GetSavedCash(c, tx, userId);
                        var free = Math.Max(0m, allCash - saved);
                        if (free < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");
                        SubCash(c, tx, userId, amount);
                        break;
                    }

                // SavedCash
                case 1:
                    SubSaved(c, tx, userId, amount);
                    break;

                // Envelope
                case 2:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla koperty.");
                    SubEnvelopeAllocated(c, tx, userId, paymentRefId.Value, amount);
                    break;

                // BankAccount
                case 3:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla konta bankowego.");
                    SubBank(c, tx, userId, paymentRefId.Value, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieznany PaymentKind={paymentKind}.");
            }
        }

        public static void RevertExpenseEffect(
            SqliteConnection c,
            SqliteTransaction tx,
            int userId,
            decimal amount,
            int paymentKind,
            int? paymentRefId)
        {
            EnsureNonNegative(amount, nameof(amount));

            switch (paymentKind)
            {
                // FreeCash
                case 0:
                    AddCash(c, tx, userId, amount);
                    break;

                // SavedCash
                case 1:
                    AddSaved(c, tx, userId, amount);
                    break;

                // Envelope
                case 2:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla koperty.");
                    AddEnvelopeAllocated(c, tx, userId, paymentRefId.Value, amount);
                    break;

                // BankAccount
                case 3:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla konta bankowego.");
                    AddBank(c, tx, userId, paymentRefId.Value, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieznany PaymentKind={paymentKind}.");
            }
        }



        // =========================
        //  Spend* (zgodne z TransactionsFacadeService)
        // =========================
        public static void SpendFromFreeCash(int userId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();
            ApplyExpenseEffect(c, tx, userId, amount, paymentKind: 0, paymentRefId: null);
            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromSavedCash(int userId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();
            ApplyExpenseEffect(c, tx, userId, amount, paymentKind: 1, paymentRefId: null);
            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromEnvelope(int userId, int envelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();
            ApplyExpenseEffect(c, tx, userId, amount, paymentKind: 2, paymentRefId: envelopeId);
            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromBankAccount(int userId, int accountId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();
            ApplyExpenseEffect(c, tx, userId, amount, paymentKind: 3, paymentRefId: accountId);
            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }




        // ============================================================
        //  PUBLIC API – zachowujemy istniejące metody,
        //  ale one TERAZ zapisują rekord Transfers
        // ============================================================

        public static int TransferFreeToSaved(int userId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_FREE, null, KIND_SAVED, null, isPlanned: false);

        public static int TransferSavedToFree(int userId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_SAVED, null, KIND_FREE, null, isPlanned: false);

        public static int TransferSavedToBank(int userId, int accountId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_SAVED, null, KIND_BANK, accountId, isPlanned: false);

        public static int TransferBankToSaved(int userId, int accountId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_BANK, accountId, KIND_SAVED, null, isPlanned: false);

        public static int TransferBankToBank(int userId, int fromAccountId, int toAccountId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_BANK, fromAccountId, KIND_BANK, toAccountId, isPlanned: false);

        public static int TransferSavedToEnvelope(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_SAVED, null, KIND_ENV, envelopeId, isPlanned: false);

        public static int TransferEnvelopeToSaved(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_ENV, envelopeId, KIND_SAVED, null, isPlanned: false);

        public static int TransferFreeToEnvelope(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_FREE, null, KIND_ENV, envelopeId, isPlanned: false);

        public static int TransferEnvelopeToFree(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_ENV, envelopeId, KIND_FREE, null, isPlanned: false);

        public static int TransferEnvelopeToEnvelope(int userId, int fromEnvelopeId, int toEnvelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => TransferAny(userId, amount, date ?? DateTime.Today, desc, KIND_ENV, fromEnvelopeId, KIND_ENV, toEnvelopeId, isPlanned: false);

        // ============================================================
        //  DELETE (jak miałaś) – tu zostawiamy Twoją logikę,
        //  ale teraz transfery będą istnieć w Transfers, więc będą kasowalne i widoczne.
        // ============================================================

        public static void DeleteTransactionAndRevertBalance(int transactionId)
        {
            if (transactionId <= 0) return;

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // 1) TRANSFERS – reverse transfer
            if (DatabaseService.TableExists(c, "Transfers"))
            {
                using var get = c.CreateCommand();
                get.Transaction = tx;

                var hasPlanned = DatabaseService.ColumnExists(c, "Transfers", "IsPlanned");

                get.CommandText = hasPlanned
                    ? @"SELECT UserId, Amount, FromKind, FromRefId, ToKind, ToRefId, IsPlanned
                        FROM Transfers WHERE Id=@id LIMIT 1;"
                    : @"SELECT UserId, Amount, FromKind, FromRefId, ToKind, ToRefId, 0 as IsPlanned
                        FROM Transfers WHERE Id=@id LIMIT 1;";

                get.Parameters.AddWithValue("@id", transactionId);

                using var r = get.ExecuteReader();
                if (r.Read())
                {
                    var userId = r.GetInt32(0);
                    var amount = Convert.ToDecimal(r.GetValue(1));
                    var fromKind = r.IsDBNull(2) ? "" : r.GetString(2);
                    var fromRef = r.IsDBNull(3) ? (int?)null : r.GetInt32(3);
                    var toKind = r.IsDBNull(4) ? "" : r.GetString(4);
                    var toRef = r.IsDBNull(5) ? (int?)null : r.GetInt32(5);
                    var isPlanned = !r.IsDBNull(6) && Convert.ToInt32(r.GetValue(6)) == 1;

                    if (!isPlanned)
                    {
                        // reverse = To -> From
                        ApplyTransferEffect(c, tx, userId, toKind, toRef, fromKind, fromRef, amount);
                    }

                    using var del = c.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM Transfers WHERE Id=@id;";
                    del.Parameters.AddWithValue("@id", transactionId);
                    del.ExecuteNonQuery();

                    tx.Commit();
                    DatabaseService.NotifyDataChanged();
                    return;
                }
            }

            // Jeśli nie jest transferem – zostaw resztę Twojej logiki (Expenses/Incomes)
        }
    }
}
