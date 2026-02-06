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
            bool hasPk = DatabaseService.ColumnExists(c, "Transfers", "FromPaymentKind")
                      && DatabaseService.ColumnExists(c, "Transfers", "ToPaymentKind");

            if (hasPk)
            {
                // Mapowanie string-kind -> PaymentKind (Twoje stałe KIND_*)
                int MapPk(string k) => NormKind(k) switch
                {
                    KIND_FREE => (int)PaymentKind.FreeCash,
                    KIND_SAVED => (int)PaymentKind.SavedCash,
                    KIND_ENV => (int)PaymentKind.Envelope,
                    KIND_BANK => (int)PaymentKind.BankAccount,
                    KIND_LEGACY_CASH => (int)PaymentKind.FreeCash,
                    _ => throw new InvalidOperationException($"Nieznany kind: '{k}'.")
                };

                var fromPk = MapPk(fromKind);
                var toPk = MapPk(toKind);

                cmd.CommandText = @"
INSERT INTO Transfers(
    UserId, Amount, Date, Description,
    FromPaymentKind, FromPaymentRefId,
    ToPaymentKind, ToPaymentRefId,
    FromKind, FromRefId, ToKind, ToRefId,
    IsPlanned
)
VALUES(
    @u,@a,@d,@desc,
    @fpk,@fpr,
    @tpk,@tpr,
    @fk,@fr,@tk,@tr,
    @p
);
SELECT last_insert_rowid();";

                cmd.Parameters.AddWithValue("@fpk", fromPk);
                cmd.Parameters.AddWithValue("@fpr", (object?)fromRefId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@tpk", toPk);
                cmd.Parameters.AddWithValue("@tpr", (object?)toRefId ?? DBNull.Value);
            }
            else
            {
                // stara baza – tylko legacy kolumny
                cmd.CommandText = @"
INSERT INTO Transfers(UserId, Amount, Date, Description, FromKind, FromRefId, ToKind, ToRefId, IsPlanned)
VALUES(@u,@a,@d,@desc,@fk,@fr,@tk,@tr,@p);
SELECT last_insert_rowid();";
            }


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

        private static decimal GetEnvelopesAllocatedSum(SqliteConnection c, SqliteTransaction tx, int userId)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COALESCE(SUM(Allocated),0) FROM Envelopes WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
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
        public static void ApplyTransferEffect(
    SqliteConnection c, SqliteTransaction tx,
    int userId, decimal amount,
    int fromPaymentKind, int? fromPaymentRefId,
    int toPaymentKind, int? toPaymentRefId)
        {
            EnsureNonNegative(amount, nameof(amount));

            if (!Enum.IsDefined(typeof(PaymentKind), fromPaymentKind))
                throw new InvalidOperationException($"Nieznany PaymentKind FROM={fromPaymentKind}.");

            if (!Enum.IsDefined(typeof(PaymentKind), toPaymentKind))
                throw new InvalidOperationException($"Nieznany PaymentKind TO={toPaymentKind}.");

            string KindFromPk(int pk) => ((PaymentKind)pk) switch
            {
                PaymentKind.FreeCash => KIND_FREE,
                PaymentKind.SavedCash => KIND_SAVED,
                PaymentKind.Envelope => KIND_ENV,
                PaymentKind.BankAccount => KIND_BANK,
                _ => throw new InvalidOperationException($"Nieobsługiwany PaymentKind={pk}.")
            };

            // Mapujemy PaymentKind -> string kind i lecimy JEDNĄ logiką (Twoją, z Free/Saved/Envelope)
            ApplyTransferEffect(
                c, tx,
                userId,
                KindFromPk(fromPaymentKind), fromPaymentRefId,
                KindFromPk(toPaymentKind), toPaymentRefId,
                amount);
        }

        private static void ApplyTransferEffect(
            SqliteConnection c, SqliteTransaction tx,
            int userId,
            string fromKind, int? fromRefId,
            string toKind, int? toRefId,
            decimal amount)
        {
            fromKind = NormKind(fromKind);
            toKind = NormKind(toKind);

            // ✅ KLUCZ: legacy "cash" traktujemy jak "freecash"
            if (fromKind == KIND_LEGACY_CASH) fromKind = KIND_FREE;
            if (toKind == KIND_LEGACY_CASH) toKind = KIND_FREE;


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

            // 7) Envelope -> Bank (REALNY wypływ gotówki do banku)
            if (fromKind == KIND_ENV && toKind == KIND_BANK)
            {
                if (!fromRefId.HasValue) throw new InvalidOperationException("Brak FromRefId dla envelope.");
                if (!toRefId.HasValue) throw new InvalidOperationException("Brak ToRefId dla bank.");

                // zdejmujemy z koperty
                SubEnvelopeAllocated(c, tx, userId, fromRefId.Value, amount);

                // bo koperty siedzą w saved cash (i w total cash)
                SubSaved(c, tx, userId, amount);
                SubCash(c, tx, userId, amount);

                // dodajemy na konto bankowe
                AddBank(c, tx, userId, toRefId.Value, amount);
                return;
            }

            // 8) Bank -> Envelope (REALNY wpływ gotówki z banku do koperty)
            if (fromKind == KIND_BANK && toKind == KIND_ENV)
            {
                if (!fromRefId.HasValue) throw new InvalidOperationException("Brak FromRefId dla bank.");
                if (!toRefId.HasValue) throw new InvalidOperationException("Brak ToRefId dla envelope.");

                // zdejmujemy z banku
                SubBank(c, tx, userId, fromRefId.Value, amount);

                // gotówka rośnie (i saved rośnie, bo koperta = saved+allocated)
                AddCash(c, tx, userId, amount);
                AddSaved(c, tx, userId, amount);
                AddEnvelopeAllocated(c, tx, userId, toRefId.Value, amount);
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
            EnsureRow(c, tx, "CashOnHand", userId);
            EnsureRow(c, tx, "SavedCash", userId);

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
                    {
                        // Odłożona gotówka do wydania = SavedCash - suma alokacji kopert
                        var saved = GetSavedCash(c, tx, userId);
                        var allocatedSum = GetEnvelopesAllocatedSum(c, tx, userId);
                        var unallocated = saved - allocatedSum;

                        if (amount > unallocated)
                            throw new InvalidOperationException(
                                $"Brak wolnych środków w „Odłożonej gotówce”. Dostępne do wydania: {unallocated.ToString("N2", CultureInfo.CurrentCulture)} zł. " +
                                "Wybierz kopertę albo zmniejsz przydział kopert."
                            );

                        SubSaved(c, tx, userId, amount);
                        SubCash(c, tx, userId, amount);
                        break;
                    }



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
        //  INCOME EFFECT (symetria do Expenses)
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
            EnsureRow(c, tx, "CashOnHand", userId);
            EnsureRow(c, tx, "SavedCash", userId);


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

            var pk = (PaymentKind)paymentKind;
            switch (pk)
            {
                case PaymentKind.FreeCash:
                    SubCash(c, tx, userId, amount);
                    break;

                case PaymentKind.SavedCash:
                    // saved to subset gotówki
                    SubSaved(c, tx, userId, amount);
                    SubCash(c, tx, userId, amount);
                    break;

                case PaymentKind.BankAccount:
                    if (!paymentRefId.HasValue)
                        throw new InvalidOperationException("Brak PaymentRefId dla konta bankowego przy cofaniu przychodu.");
                    SubBank(c, tx, userId, paymentRefId.Value, amount);
                    break;

                default:
                    throw new InvalidOperationException($"Nieobsługiwany PaymentKind={paymentKind} dla przychodu.");
            }

            // ✅ KLUCZ: po cofnięciu przychodu stabilizujemy relacje:
            // Saved <= CashOnHand oraz EnvelopesAllocated <= Saved
            NormalizeCashSavedEnvelope(c, tx, userId);
        }

        private static void NormalizeCashSavedEnvelope(SqliteConnection c, SqliteTransaction tx, int userId)
        {
            // 1) saved nie może być większe niż cashOnHand
            var cash = GetCashOnHand(c, tx, userId);
            var saved = GetSavedCash(c, tx, userId);

            if (saved > cash)
            {
                var diff = saved - cash;

                // najpierw zdejmujemy z kopert (bo koperty są częścią saved)
                ReduceEnvelopeAllocationsBy(c, tx, userId, diff);

                // po zdjęciu z kopert może się zmienić suma allocated, ale saved nadal trzeba zbić do cash
                saved = GetSavedCash(c, tx, userId);
                if (saved > cash)
                {
                    var still = saved - cash;
                    // bezpiecznie: jeśli still > 0, to znaczy, że “odłożona” gotówka istnieje bez pokrycia w cash
                    SubSaved(c, tx, userId, still);
                }
            }

            // 2) suma kopert nie może być większa niż saved
            saved = GetSavedCash(c, tx, userId);
            var allocated = GetEnvelopesAllocatedSum(c, tx, userId);

            if (allocated > saved)
            {
                var diff = allocated - saved;
                ReduceEnvelopeAllocationsBy(c, tx, userId, diff);
            }
        }

        private static void ReduceEnvelopeAllocationsBy(SqliteConnection c, SqliteTransaction tx, int userId, decimal amountToReduce)
        {
            EnsureNonNegative(amountToReduce, nameof(amountToReduce));
            if (amountToReduce <= 0m) return;

            // Pobieramy koperty z największą alokacją – z nich zdejmujemy najpierw
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, Allocated
FROM Envelopes
WHERE UserId=@u
ORDER BY Allocated DESC, Id ASC;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();

            var remaining = amountToReduce;

            while (r.Read() && remaining > 0m)
            {
                var envId = Convert.ToInt32(r.GetValue(0));
                var allocated = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
                if (allocated <= 0m) continue;

                var take = allocated >= remaining ? remaining : allocated;

                // odejmujemy z koperty
                SubEnvelopeAllocated(c, tx, userId, envId, take);

                remaining -= take;
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
            if (!DatabaseService.TableExists(c, "Transfers"))
                return false;

            bool hasPlanned = DatabaseService.ColumnExists(c, "Transfers", "IsPlanned");

            bool hasPkCols =
                DatabaseService.ColumnExists(c, "Transfers", "FromPaymentKind") &&
                DatabaseService.ColumnExists(c, "Transfers", "ToPaymentKind");

            // legacy kolumny mogą istnieć albo nie
            bool hasLegacyCols =
                DatabaseService.ColumnExists(c, "Transfers", "FromKind") &&
                DatabaseService.ColumnExists(c, "Transfers", "ToKind");

            using var get = c.CreateCommand();
            get.Transaction = tx;

            // Ujednolicamy SELECT tak, żeby zawsze mieć te same indeksy pól w readerze.
            // Kolejność:
            // 0 UserId
            // 1 Amount
            // 2 FromPaymentKind (int)
            // 3 FromPaymentRefId (int?)
            // 4 ToPaymentKind (int)
            // 5 ToPaymentRefId (int?)
            // 6 FromKind (text)
            // 7 FromRefId (int?)
            // 8 ToKind (text)
            // 9 ToRefId (int?)
            // 10 IsPlanned (int)
            if (hasPkCols)
            {
                get.CommandText = $@"
SELECT
    UserId,
    Amount,

    FromPaymentKind,
    FromPaymentRefId,
    ToPaymentKind,
    ToPaymentRefId,

    {(hasLegacyCols ? "FromKind" : "NULL")}  as FromKind,
    {(hasLegacyCols ? "FromRefId" : "NULL")} as FromRefId,
    {(hasLegacyCols ? "ToKind" : "NULL")}    as ToKind,
    {(hasLegacyCols ? "ToRefId" : "NULL")}   as ToRefId,

    {(hasPlanned ? "IsPlanned" : "0")} as IsPlanned
FROM Transfers
WHERE Id=@id
LIMIT 1;";
            }
            else
            {
                // stary transfer bez PaymentKind/RefId
                get.CommandText = $@"
SELECT
    UserId,
    Amount,

    0 as FromPaymentKind,
    NULL as FromPaymentRefId,
    0 as ToPaymentKind,
    NULL as ToPaymentRefId,

    {(hasLegacyCols ? "FromKind" : "NULL")}  as FromKind,
    {(hasLegacyCols ? "FromRefId" : "NULL")} as FromRefId,
    {(hasLegacyCols ? "ToKind" : "NULL")}    as ToKind,
    {(hasLegacyCols ? "ToRefId" : "NULL")}   as ToRefId,

    {(hasPlanned ? "IsPlanned" : "0")} as IsPlanned
FROM Transfers
WHERE Id=@id
LIMIT 1;";
            }

            get.Parameters.AddWithValue("@id", id);

            using var r = get.ExecuteReader();
            if (!r.Read())
                return false;

            int userId = r.GetInt32(0);
            decimal amount = Convert.ToDecimal(r.GetValue(1));

            int fromPk = Convert.ToInt32(r.GetValue(2));
            int? fromPkRef = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetValue(3));

            int toPk = Convert.ToInt32(r.GetValue(4));
            int? toPkRef = r.IsDBNull(5) ? (int?)null : Convert.ToInt32(r.GetValue(5));

            string fromKindLegacy = r.IsDBNull(6) ? "" : (r.GetString(6) ?? "");
            int? fromRefLegacy = r.IsDBNull(7) ? (int?)null : Convert.ToInt32(r.GetValue(7));

            string toKindLegacy = r.IsDBNull(8) ? "" : (r.GetString(8) ?? "");
            int? toRefLegacy = r.IsDBNull(9) ? (int?)null : Convert.ToInt32(r.GetValue(9));

            bool isPlanned = !r.IsDBNull(10) && Convert.ToInt32(r.GetValue(10)) == 1;

            // 1) Najpierw próbujemy użyć legacy (jeśli jest sensowne), bo ApplyTransferEffect u Ciebie działa na "kind string".
            // 2) Jeśli legacy puste, mapujemy PaymentKind -> string kind używany przez księgowanie transferów.
            static string MapPaymentKindToLegacy(int pk)
            {
                return ((PaymentKind)pk) switch
                {
                    PaymentKind.FreeCash => "freecash",
                    PaymentKind.SavedCash => "savedcash",
                    PaymentKind.BankAccount => "bank",
                    PaymentKind.Envelope => "envelope",
                    _ => "freecash"
                };
            }


            string fromKind = !string.IsNullOrWhiteSpace(fromKindLegacy) ? fromKindLegacy : MapPaymentKindToLegacy(fromPk);
            int? fromRef = fromRefLegacy ?? fromPkRef;

            string toKind = !string.IsNullOrWhiteSpace(toKindLegacy) ? toKindLegacy : MapPaymentKindToLegacy(toPk);
            int? toRef = toRefLegacy ?? toPkRef;

            if (!isPlanned)
            {
                if (hasPkCols)
                {
                    // reverse = TO -> FROM (PaymentKind)
                    ApplyTransferEffect(c, tx, userId, amount, toPk, toPkRef, fromPk, fromPkRef);
                }
                else
                {
                    // reverse = TO -> FROM (legacy string-kind)
                    ApplyTransferEffect(c, tx, userId, toKind, toRef, fromKind, fromRef, amount);
                }
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
            bool hasSource = DatabaseService.ColumnExists(c, "Incomes", "Source");

            using var get = c.CreateCommand();
            get.Transaction = tx;

            get.CommandText = $@"
SELECT UserId,
       Amount,
       {(hasPlanned ? "IsPlanned" : "0")} as IsPlanned,
       {(hasPk ? "PaymentKind" : "0")} as PaymentKind,
       {(hasPr ? "PaymentRefId" : "NULL")} as PaymentRefId,
       {(hasSource ? "Source" : "NULL")} as Source
FROM Incomes
WHERE Id=@id
LIMIT 1;";
            get.Parameters.AddWithValue("@id", id);

            using var r = get.ExecuteReader();
            if (!r.Read()) return false;

            int userId = Convert.ToInt32(r.GetValue(0));
            decimal amount = Math.Abs(Convert.ToDecimal(r.GetValue(1)));
            bool isPlanned = Convert.ToInt32(r.GetValue(2)) == 1;

            int pk = Convert.ToInt32(r.GetValue(3));
            int? pr = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4));
            string source = r.IsDBNull(5) ? "" : (r.GetString(5) ?? "");

            // 1) Zaplanowane -> nie odwracamy sald
            if (!isPlanned)
            {
                // ───────────── normalizacja / walidacja pk/pr ─────────────
                bool pkDefined = Enum.IsDefined(typeof(PaymentKind), pk);
                bool pkIsNoneOrInvalid = (!hasPk) || (!pkDefined) || pk == 0;

                bool pkNeedsRef = pk == (int)PaymentKind.BankAccount || pk == (int)PaymentKind.Envelope;
                bool pkRefMismatch = pkNeedsRef && !pr.HasValue;

                // Source bywa: "Wolna gotówka", "Odłożona gotówka", "Konto: mBank", "Konto bankowe: mBank", "Koperta: Jedzenie"
                string s = (source ?? "").Trim();

                bool sourceHintsBank =
                    !string.IsNullOrWhiteSpace(s) &&
                    (s.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase) ||
                     s.StartsWith("Konto bankowe:", StringComparison.OrdinalIgnoreCase) ||
                     s.StartsWith("Konto", StringComparison.OrdinalIgnoreCase)); // ostatnie jako broad-match

                bool sourceHintsEnv =
                    !string.IsNullOrWhiteSpace(s) &&
                    (s.StartsWith("Koperta:", StringComparison.OrdinalIgnoreCase) ||
                     s.StartsWith("Koperta", StringComparison.OrdinalIgnoreCase));

                bool pkLooksCashButSourceIsSpecific =
                    (pk == 0 ||
                     pk == (int)PaymentKind.FreeCash ||
                     pk == (int)PaymentKind.SavedCash) && (sourceHintsBank || sourceHintsEnv);

                bool needInference = pkIsNoneOrInvalid || pkRefMismatch || pkLooksCashButSourceIsSpecific;

                if (needInference)
                {
                    // 2) Najpierw próbuj z Source (to jest najbardziej wiarygodne),
                    //    a jak się nie da, to fallbackuj po PaymentRefId w tabelach.
                    try
                    {
                        InferIncomeDestinationFromSource(c, tx, userId, s, out pk, out pr);
                    }
                    catch
                    {
                        // fallback: jeśli pr istnieje, sprawdź czy to bank albo koperta
                        if (pr.HasValue)
                        {
                            // bank?
                            if (DatabaseService.TableExists(c, "BankAccounts") &&
                                DatabaseService.ColumnExists(c, "BankAccounts", "Id"))
                            {
                                using var chk = c.CreateCommand();
                                chk.Transaction = tx;
                                chk.CommandText = "SELECT 1 FROM BankAccounts WHERE UserId=@u AND Id=@id LIMIT 1;";
                                chk.Parameters.AddWithValue("@u", userId);
                                chk.Parameters.AddWithValue("@id", pr.Value);
                                var exists = chk.ExecuteScalar();
                                if (exists != null)
                                {
                                    pk = (int)PaymentKind.BankAccount;
                                }
                            }

                            // koperta? (tylko jeśli nie ustawiliśmy banku)
                            if (pk != (int)PaymentKind.BankAccount &&
                                DatabaseService.TableExists(c, "Envelopes") &&
                                DatabaseService.ColumnExists(c, "Envelopes", "Id"))
                            {
                                using var chk = c.CreateCommand();
                                chk.Transaction = tx;
                                chk.CommandText = "SELECT 1 FROM Envelopes WHERE UserId=@u AND Id=@id LIMIT 1;";
                                chk.Parameters.AddWithValue("@u", userId);
                                chk.Parameters.AddWithValue("@id", pr.Value);
                                var exists = chk.ExecuteScalar();
                                if (exists != null)
                                {
                                    pk = (int)PaymentKind.Envelope;
                                }
                            }

                            // jeśli nadal nie wiemy co to jest — traktuj jak wolną gotówkę i wyczyść RefId
                            if (pk != (int)PaymentKind.BankAccount && pk != (int)PaymentKind.Envelope)
                            {
                                pk = (int)PaymentKind.FreeCash;
                                pr = null;
                            }
                        }
                        else
                        {
                            // brak RefId -> nie ma sensu zgadywać bank/koperty
                            // Source nie zadziałał => safest: wolna gotówka
                            pk = (int)PaymentKind.FreeCash;
                            pr = null;
                        }
                    }
                }

                // 3) Ostateczna walidacja (żeby nie wywalić księgowania)
                if (pk == (int)PaymentKind.BankAccount || pk == (int)PaymentKind.Envelope)
                {
                    if (!pr.HasValue)
                    {
                        // tu już NIE próbujemy “wymyślać” — jeśli brak RefId,
                        // to cofnięcie zrobiłoby syf. Najbezpieczniej przerwać.
                        throw new InvalidOperationException(
                            $"Nie można odwrócić przychodu {id}: PaymentKind wymaga PaymentRefId, ale RefId jest NULL (Source='{source}').");
                    }
                }

                RevertIncomeEffect(c, tx, userId, amount, pk, pr);
            }

            // 4) usuń rekord
            using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Incomes WHERE Id=@id;";
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();

            return true;
        }




        private static void InferIncomeDestinationFromSource(
            SqliteConnection c,
            SqliteTransaction tx,
            int userId,
            string source,
            out int paymentKind,
            out int? paymentRefId)
        {
            paymentRefId = null;

            var s = (source ?? "").Trim();

            // Dopasuj do tego, jak TY realnie zapisujesz Source w DB.
            // Z Twoich VM wynika, że Source bywa np: "Wolna gotówka", "Odłożona gotówka", "Konto: mBank", "Koperta: Jedzenie"
            if (s.Equals("Wolna gotówka", StringComparison.OrdinalIgnoreCase))
            {
                paymentKind = (int)PaymentKind.FreeCash;
                return;
            }

            if (s.Equals("Odłożona gotówka", StringComparison.OrdinalIgnoreCase))
            {
                paymentKind = (int)PaymentKind.SavedCash;
                return;
            }

            if (s.StartsWith("Konto", StringComparison.OrdinalIgnoreCase))
            {
                paymentKind = (int)PaymentKind.BankAccount;

                // spróbuj wyciągnąć nazwę po ":" (np. "Konto: mBank")
                var name = s;
                var idx = s.IndexOf(':');
                if (idx >= 0 && idx + 1 < s.Length) name = s[(idx + 1)..].Trim();

                paymentRefId = ResolveBankAccountIdByName(c, tx, userId, name);
                if (!paymentRefId.HasValue)
                    throw new InvalidOperationException($"Nie można ustalić konta bankowego dla przychodu (Source='{source}').");

                return;
            }

            if (s.StartsWith("Koperta", StringComparison.OrdinalIgnoreCase))
            {
                paymentKind = (int)PaymentKind.Envelope;

                var name = s;
                var idx = s.IndexOf(':');
                if (idx >= 0 && idx + 1 < s.Length) name = s[(idx + 1)..].Trim();

                paymentRefId = ResolveEnvelopeIdByName(c, tx, userId, name);
                if (!paymentRefId.HasValue)
                    throw new InvalidOperationException($"Nie można ustalić koperty dla przychodu (Source='{source}').");

                return;
            }

            // fallback – jeśli nie rozpoznaliśmy formatu Source
            paymentKind = (int)PaymentKind.FreeCash;
            paymentRefId = null;
        }

        private static int? ResolveBankAccountIdByName(SqliteConnection c, SqliteTransaction tx, int userId, string accountName)
        {
            if (!DatabaseService.TableExists(c, "BankAccounts")) return null;

            // wybierz istniejącą kolumnę z nazwą konta
            string? col =
                DatabaseService.ColumnExists(c, "BankAccounts", "Name") ? "Name" :
                DatabaseService.ColumnExists(c, "BankAccounts", "AccountName") ? "AccountName" :
                DatabaseService.ColumnExists(c, "BankAccounts", "Title") ? "Title" :
                DatabaseService.ColumnExists(c, "BankAccounts", "BankName") ? "BankName" :
                null;

            if (col == null)
                throw new InvalidOperationException("Tabela BankAccounts nie ma kolumny z nazwą (Name/AccountName/Title/BankName).");

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $@"
SELECT Id
FROM BankAccounts
WHERE UserId=@u AND (TRIM({col})=TRIM(@n))
LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", accountName);

            var obj = cmd.ExecuteScalar();
            return obj == null || obj == DBNull.Value ? (int?)null : Convert.ToInt32(obj);
        }

        private static int? ResolveEnvelopeIdByName(SqliteConnection c, SqliteTransaction tx, int userId, string envelopeName)
        {
            if (!DatabaseService.TableExists(c, "Envelopes")) return null;

            // wybierz istniejącą kolumnę z nazwą koperty
            string? col =
                DatabaseService.ColumnExists(c, "Envelopes", "Name") ? "Name" :
                DatabaseService.ColumnExists(c, "Envelopes", "Title") ? "Title" :
                DatabaseService.ColumnExists(c, "Envelopes", "EnvelopeName") ? "EnvelopeName" :
                null;

            if (col == null)
                throw new InvalidOperationException("Tabela Envelopes nie ma kolumny z nazwą (Name/Title/EnvelopeName).");

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $@"
SELECT Id
FROM Envelopes
WHERE UserId=@u AND (TRIM({col})=TRIM(@n))
LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", envelopeName);

            var obj = cmd.ExecuteScalar();
            return obj == null || obj == DBNull.Value ? (int?)null : Convert.ToInt32(obj);
        }


        private static void EnsureRow(SqliteConnection con, SqliteTransaction tx, string table, int userId)
        {
            // Minimalna wersja: zakłada kolumny UserId, Amount, a UpdatedAt może istnieć lub nie
            bool hasUpdatedAt = DatabaseService.ColumnExists(con, table, "UpdatedAt");

            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = hasUpdatedAt
                ? $@"INSERT OR IGNORE INTO {table}(UserId, Amount, UpdatedAt) VALUES(@u, 0, CURRENT_TIMESTAMP);"
                : $@"INSERT OR IGNORE INTO {table}(UserId, Amount) VALUES(@u, 0);";

            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }

        // Minimalna wersja: zakłada kolumny UserId, Amount, a UpdatedAt może istnieć lub nie
        /// <summary>
        /// Publiczne API usuwania przychodu.
        /// Spójne z resztą LedgerService: transakcja + odwrócenie efektu tylko jeśli nie-planned.
        /// </summary>
        public static void DeleteIncome(int incomeId)
        {
            if (incomeId <= 0) return;

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // To robi:
            // - jeśli planned: tylko DELETE rekordu (bez ruszania sald)
            // - jeśli zrealizowany: RevertIncomeEffect + DELETE
            if (TryDeleteIncomeInternal(c, tx, incomeId))
            {
                tx.Commit();
                DatabaseService.NotifyDataChanged();
                return;
            }

            tx.Commit(); // nic do usunięcia
        }

        private static void DeleteIncomeInternal(SqliteConnection c, SqliteTransaction tx, int id)
        {
            if (!TryDeleteIncomeInternal(c, tx, id))
                throw new InvalidOperationException("Nie znaleziono przychodu do usunięcia.");
        }
    }
}
