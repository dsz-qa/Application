using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using Finly.Models;

namespace Finly.Services.Features
{
    /// <summary>
    /// Źródło prawdy księgowania w aplikacji:
    /// - wpływ na salda (CashOnHand / SavedCash / Envelopes / BankAccounts)
    /// - odwracanie skutków przy usuwaniu transakcji
    /// </summary>
    public static class LedgerService
    {
        // Ujednolicony typ transakcji (eliminuje kolizję Id pomiędzy tabelami).
        public enum TransactionSource
        {
            Transfer = 0,
            Expense = 1,
            Income = 2
        }

        private static string ToIsoDate(DateTime dt)
            => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
            // Jeśli SchemaService/DatabaseService już to robi – to nie zaszkodzi.
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
                if (obj == null || obj == DBNull.Value)
                    throw new InvalidOperationException("Nie znaleziono rachunku bankowego lub nie należy do użytkownika.");

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

            if (fromKind == toKind && fromRefId == toRefId) return;

            // Helper: dostępna wolna gotówka (free = cashOnHandTotal - savedTotal)
            decimal GetFree()
            {
                var allCash = GetCashOnHand(c, tx, userId);
                var saved = GetSavedCash(c, tx, userId);
                var free = allCash - saved;
                return free < 0m ? 0m : free;
            }

            // ====== SPECJALNE KOMBINACJE (Twoja logika kopert) ======

            // 1) Free -> Saved (zmiana podziału, bez zmiany CashOnHand)
            if (fromKind == KIND_FREE && toKind == KIND_SAVED)
            {
                if (GetFree() < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");
                AddSaved(c, tx, userId, amount);
                return;
            }

            // 2) Saved -> Free (zmiana podziału, bez zmiany CashOnHand)
            if (fromKind == KIND_SAVED && toKind == KIND_FREE)
            {
                SubSaved(c, tx, userId, amount);
                return;
            }

            // 3) Saved -> Envelope (alokacja z puli Saved do koperty: Saved bez zmian, rośnie tylko Allocated)
            if (fromKind == KIND_SAVED && toKind == KIND_ENV)
            {
                if (!toRefId.HasValue) throw new InvalidOperationException("Brak ToRefId dla envelope.");
                AddEnvelopeAllocated(c, tx, userId, toRefId.Value, amount);
                return;
            }

            // 4) Envelope -> Saved (od-alokowanie: maleje Allocated, Saved bez zmian)
            if (fromKind == KIND_ENV && toKind == KIND_SAVED)
            {
                if (!fromRefId.HasValue) throw new InvalidOperationException("Brak FromRefId dla envelope.");
                SubEnvelopeAllocated(c, tx, userId, fromRefId.Value, amount);
                return;
            }

            // 5) Free -> Envelope (z wolnej gotówki robisz “odłożoną + alokację”, żeby free spadło)
            if (fromKind == KIND_FREE && toKind == KIND_ENV)
            {
                if (!toRefId.HasValue) throw new InvalidOperationException("Brak ToRefId dla envelope.");
                if (GetFree() < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");

                AddSaved(c, tx, userId, amount); // free spadnie (bo saved rośnie)
                AddEnvelopeAllocated(c, tx, userId, toRefId.Value, amount);
                return;
            }

            // 6) Envelope -> Free (odwrotność: zdejmujesz alokację i zmniejszasz Saved)
            if (fromKind == KIND_ENV && toKind == KIND_FREE)
            {
                if (!fromRefId.HasValue) throw new InvalidOperationException("Brak FromRefId dla envelope.");

                SubEnvelopeAllocated(c, tx, userId, fromRefId.Value, amount);
                SubSaved(c, tx, userId, amount); // free wzrośnie, bo saved maleje
                return;
            }

            // ====== FALLBACK DLA POZOSTAŁYCH (banki, env->env, bank<->cash itd.) ======
            // Zdejmij z FROM, dodaj do TO.
            // Uwaga: KIND_FREE w fallbacku oznacza REALNĄ gotówkę w portfelu (CashOnHand) – czyli wpływa na total.

            // ---- FROM ----
            switch (fromKind)
            {
                case KIND_FREE:
                    if (GetFree() < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");
                    SubCash(c, tx, userId, amount);
                    break;

                case KIND_SAVED:
                    SubSaved(c, tx, userId, amount);
                    SubCash(c, tx, userId, amount);
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

            // ---- TO ----
            switch (toKind)
            {
                case KIND_FREE:
                    AddCash(c, tx, userId, amount);
                    break;

                case KIND_SAVED:
                    AddCash(c, tx, userId, amount);
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
        //  EXPENSE EFFECT (źródło prawdy: Finly.Models.PaymentKind)
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

            decimal GetFree()
            {
                var allCash = GetCashOnHand(c, tx, userId);
                var saved = GetSavedCash(c, tx, userId);
                var free = allCash - saved;
                return free < 0m ? 0m : free;
            }

            if (!Enum.IsDefined(typeof(PaymentKind), paymentKind))
                throw new InvalidOperationException($"Nieznany PaymentKind={paymentKind}.");

            var pk = (PaymentKind)paymentKind;

            switch (pk)
            {
                case PaymentKind.FreeCash:
                    if (GetFree() < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");
                    SubCash(c, tx, userId, amount);
                    break;

                case PaymentKind.SavedCash:
                    SubSaved(c, tx, userId, amount);
                    SubCash(c, tx, userId, amount);
                    break;

                case PaymentKind.BankAccount:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla konta bankowego.");
                    SubBank(c, tx, userId, paymentRefId.Value, amount);
                    break;

                case PaymentKind.Envelope:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla koperty.");
                    SubEnvelopeAllocated(c, tx, userId, paymentRefId.Value, amount);
                    SubSaved(c, tx, userId, amount);
                    SubCash(c, tx, userId, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieobsługiwany PaymentKind={paymentKind}.");
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

            if (!Enum.IsDefined(typeof(PaymentKind), paymentKind))
                throw new InvalidOperationException($"Nieznany PaymentKind={paymentKind}.");

            var pk = (PaymentKind)paymentKind;

            switch (pk)
            {
                case PaymentKind.FreeCash:
                    AddCash(c, tx, userId, amount);
                    break;

                case PaymentKind.SavedCash:
                    AddSaved(c, tx, userId, amount);
                    AddCash(c, tx, userId, amount);
                    break;

                case PaymentKind.BankAccount:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla konta bankowego.");
                    AddBank(c, tx, userId, paymentRefId.Value, amount);
                    break;

                case PaymentKind.Envelope:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla koperty.");
                    AddEnvelopeAllocated(c, tx, userId, paymentRefId.Value, amount);
                    AddSaved(c, tx, userId, amount);
                    AddCash(c, tx, userId, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieobsługiwany PaymentKind={paymentKind}.");
            }
        }

        // =========================
        //  INCOME EFFECT (NOWE – symetria do Expenses)
        // =========================
        public static void ApplyIncomeEffect(
            SqliteConnection c,
            SqliteTransaction tx,
            int userId,
            decimal amount,
            int paymentKind,
            int? paymentRefId)
        {
            EnsureNonNegative(amount, nameof(amount));

            if (!Enum.IsDefined(typeof(PaymentKind), paymentKind))
                throw new InvalidOperationException($"Nieznany PaymentKind={paymentKind}.");

            var pk = (PaymentKind)paymentKind;

            switch (pk)
            {
                case PaymentKind.FreeCash:
                    AddCash(c, tx, userId, amount);
                    break;

                case PaymentKind.SavedCash:
                    AddCash(c, tx, userId, amount);
                    AddSaved(c, tx, userId, amount);
                    break;

                case PaymentKind.BankAccount:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla konta bankowego.");
                    AddBank(c, tx, userId, paymentRefId.Value, amount);
                    break;

                case PaymentKind.Envelope:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla koperty.");
                    // “przychód do koperty” = rośnie total cash + saved + allocated
                    AddCash(c, tx, userId, amount);
                    AddSaved(c, tx, userId, amount);
                    AddEnvelopeAllocated(c, tx, userId, paymentRefId.Value, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieobsługiwany PaymentKind={paymentKind}.");
            }
        }

        public static void RevertIncomeEffect(
            SqliteConnection c,
            SqliteTransaction tx,
            int userId,
            decimal amount,
            int paymentKind,
            int? paymentRefId)
        {
            EnsureNonNegative(amount, nameof(amount));

            if (!Enum.IsDefined(typeof(PaymentKind), paymentKind))
                throw new InvalidOperationException($"Nieznany PaymentKind={paymentKind}.");

            var pk = (PaymentKind)paymentKind;

            switch (pk)
            {
                case PaymentKind.FreeCash:
                    SubCash(c, tx, userId, amount);
                    break;

                case PaymentKind.SavedCash:
                    SubSaved(c, tx, userId, amount);
                    SubCash(c, tx, userId, amount);
                    break;

                case PaymentKind.BankAccount:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla konta bankowego.");
                    SubBank(c, tx, userId, paymentRefId.Value, amount);
                    break;

                case PaymentKind.Envelope:
                    if (!paymentRefId.HasValue) throw new InvalidOperationException("Brak PaymentRefId dla koperty.");
                    SubEnvelopeAllocated(c, tx, userId, paymentRefId.Value, amount);
                    SubSaved(c, tx, userId, amount);
                    SubCash(c, tx, userId, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieobsługiwany PaymentKind={paymentKind}.");
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

            ApplyExpenseEffect(c, tx, userId, amount, (int)PaymentKind.FreeCash, paymentRefId: null);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromSavedCash(int userId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            ApplyExpenseEffect(c, tx, userId, amount, (int)PaymentKind.SavedCash, paymentRefId: null);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromEnvelope(int userId, int envelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            ApplyExpenseEffect(c, tx, userId, amount, (int)PaymentKind.Envelope, paymentRefId: envelopeId);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromBankAccount(int userId, int accountId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            ApplyExpenseEffect(c, tx, userId, amount, (int)PaymentKind.BankAccount, paymentRefId: accountId);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        // ============================================================
        //  PUBLIC API – transfery (jak miałaś)
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
        //  DELETE – typowane (bezpieczne) + kompatybilne (heurystyka)
        // ============================================================

        public static void DeleteTransactionAndRevertBalance(TransactionSource src, int transactionId)
        {
            if (transactionId <= 0) return;

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            switch (src)
            {
                case TransactionSource.Transfer:
                    DeleteTransferInternal(c, tx, transactionId);
                    break;

                case TransactionSource.Expense:
                    DeleteExpenseInternal(c, tx, transactionId);
                    break;

                case TransactionSource.Income:
                    DeleteIncomeInternal(c, tx, transactionId);
                    break;

                default:
                    throw new InvalidOperationException("Nieobsługiwany typ transakcji.");
            }

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        /// <summary>
        /// Kompatybilne API: próbuje po kolei Transfers -> Expenses -> Incomes.
        /// (Id może kolidować między tabelami, więc docelowo UI powinno używać wersji typowanej.)
        /// </summary>
        public static void DeleteTransactionAndRevertBalance(int transactionId)
        {
            if (transactionId <= 0) return;

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            if (TryDeleteTransferInternal(c, tx, transactionId) ||
                TryDeleteExpenseInternal(c, tx, transactionId) ||
                TryDeleteIncomeInternal(c, tx, transactionId))
            {
                tx.Commit();
                DatabaseService.NotifyDataChanged();
                return;
            }

            tx.Commit();
        }

        private static bool TryDeleteTransferInternal(SqliteConnection c, SqliteTransaction tx, int id)
        {
            if (!DatabaseService.TableExists(c, "Transfers")) return false;

            using var get = c.CreateCommand();
            get.Transaction = tx;

            bool hasPlanned = DatabaseService.ColumnExists(c, "Transfers", "IsPlanned");

            get.CommandText = hasPlanned
                ? @"SELECT UserId, Amount, FromKind, FromRefId, ToKind, ToRefId, IsPlanned
                    FROM Transfers WHERE Id=@id LIMIT 1;"
                : @"SELECT UserId, Amount, FromKind, FromRefId, ToKind, ToRefId, 0 as IsPlanned
                    FROM Transfers WHERE Id=@id LIMIT 1;";

            get.Parameters.AddWithValue("@id", id);

            using var r = get.ExecuteReader();
            if (!r.Read()) return false;

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
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();

            return true;
        }

        private static void DeleteTransferInternal(SqliteConnection c, SqliteTransaction tx, int id)
        {
            if (!TryDeleteTransferInternal(c, tx, id))
                throw new InvalidOperationException("Nie znaleziono transferu do usunięcia.");
        }

        private static bool TryDeleteExpenseInternal(SqliteConnection c, SqliteTransaction tx, int id)
        {
            if (!DatabaseService.TableExists(c, "Expenses")) return false;

            bool hasPlanned = DatabaseService.ColumnExists(c, "Expenses", "IsPlanned");
            bool hasPk = DatabaseService.ColumnExists(c, "Expenses", "PaymentKind");
            bool hasPr = DatabaseService.ColumnExists(c, "Expenses", "PaymentRefId");

            using var get = c.CreateCommand();
            get.Transaction = tx;
            get.CommandText = $@"
SELECT UserId,
       Amount,
       {(hasPlanned ? "IsPlanned" : "0")} as IsPlanned,
       {(hasPk ? "PaymentKind" : "0")} as PaymentKind,
       {(hasPr ? "PaymentRefId" : "NULL")} as PaymentRefId
FROM Expenses
WHERE Id=@id
LIMIT 1;";
            get.Parameters.AddWithValue("@id", id);

            using var r = get.ExecuteReader();
            if (!r.Read()) return false;

            var userId = Convert.ToInt32(r.GetValue(0));
            var amount = Convert.ToDecimal(r.GetValue(1));
            var isPlanned = Convert.ToInt32(r.GetValue(2)) == 1;
            var pk = Convert.ToInt32(r.GetValue(3));
            var pr = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4));

            if (!isPlanned)
                RevertExpenseEffect(c, tx, userId, amount, pk, pr);

            using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Expenses WHERE Id=@id;";
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();

            return true;
        }

        private static void DeleteExpenseInternal(SqliteConnection c, SqliteTransaction tx, int id)
        {
            if (!TryDeleteExpenseInternal(c, tx, id))
                throw new InvalidOperationException("Nie znaleziono wydatku do usunięcia.");
        }

        private static bool TryDeleteIncomeInternal(SqliteConnection c, SqliteTransaction tx, int id)
        {
            if (!DatabaseService.TableExists(c, "Incomes")) return false;

            bool hasPlanned = DatabaseService.ColumnExists(c, "Incomes", "IsPlanned");
            bool hasPk = DatabaseService.ColumnExists(c, "Incomes", "PaymentKind");
            bool hasPr = DatabaseService.ColumnExists(c, "Incomes", "PaymentRefId");

            using var get = c.CreateCommand();
            get.Transaction = tx;
            get.CommandText = $@"
SELECT UserId,
       Amount,
       {(hasPlanned ? "IsPlanned" : "0")} as IsPlanned,
       {(hasPk ? "PaymentKind" : "0")} as PaymentKind,
       {(hasPr ? "PaymentRefId" : "NULL")} as PaymentRefId
FROM Incomes
WHERE Id=@id
LIMIT 1;";
            get.Parameters.AddWithValue("@id", id);

            using var r = get.ExecuteReader();
            if (!r.Read()) return false;

            var userId = Convert.ToInt32(r.GetValue(0));
            var amount = Convert.ToDecimal(r.GetValue(1));
            var isPlanned = Convert.ToInt32(r.GetValue(2)) == 1;
            var pk = Convert.ToInt32(r.GetValue(3));
            var pr = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4));

            if (!isPlanned)
                RevertIncomeEffect(c, tx, userId, amount, pk, pr);

            using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Incomes WHERE Id=@id;";
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();

            return true;
        }

        private static void DeleteIncomeInternal(SqliteConnection c, SqliteTransaction tx, int id)
        {
            if (!TryDeleteIncomeInternal(c, tx, id))
                throw new InvalidOperationException("Nie znaleziono przychodu do usunięcia.");
        }
    }
}
