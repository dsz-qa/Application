using System;

namespace Finly.Services.Features
{
    /// <summary>
    /// FASADA dla UI – jedno miejsce do operacji na transakcjach/saldach.
    /// UI nie powinno wołać DatabaseService do zmiany sald.
    /// </summary>
    public static class TransactionsFacadeService
    {
        // =========================
        //  SET (ustawienia sald)
        // =========================

        public static void SetCashOnHand(int userId, decimal amount)
        {
            if (userId <= 0) throw new ArgumentException("Nieprawidłowy userId.", nameof(userId));
            if (amount < 0) throw new ArgumentException("Kwota nie może być ujemna.", nameof(amount));

            using var con = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();

            using var tx = con.BeginTransaction();
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = @"
INSERT INTO CashOnHand(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE
SET Amount   = excluded.Amount,
    UpdatedAt = CURRENT_TIMESTAMP;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SetSavedCash(int userId, decimal amount)
        {
            if (userId <= 0) throw new ArgumentException("Nieprawidłowy userId.", nameof(userId));
            if (amount < 0) throw new ArgumentException("Kwota nie może być ujemna.", nameof(amount));

            using var con = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();

            using var tx = con.BeginTransaction();
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = @"
INSERT INTO SavedCash(UserId, Amount, UpdatedAt)
VALUES (@u, @a, CURRENT_TIMESTAMP)
ON CONFLICT(UserId) DO UPDATE
SET Amount   = excluded.Amount,
    UpdatedAt = CURRENT_TIMESTAMP;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static int TransferAnyPlanned(
            int userId, decimal amount, DateTime date, string? desc,
            string fromKind, int? fromRefId,
            string toKind, int? toRefId)
            => LedgerService.TransferAny(userId, amount, date, desc, fromKind, fromRefId, toKind, toRefId, isPlanned: true);

        // =========================
        //  Delete (jedyny punkt)
        // =========================

        // Bezpieczne API (docelowe): UI przekazuje typ.
        public static void DeleteTransaction(LedgerService.TransactionSource src, int id)
            => LedgerService.DeleteTransactionAndRevertBalance(src, id);

        // Kompatybilne (awaryjne): heurystyka (transfer -> expense -> income).
        public static void DeleteTransaction(int id)
            => LedgerService.DeleteTransactionAndRevertBalance(id);

        // =========================
        //  Wydatki – UI -> Ledger
        // =========================
        public static void SpendFromFreeCash(int userId, decimal amount)
            => LedgerService.SpendFromFreeCash(userId, amount);

        public static void SpendFromSavedCash(int userId, decimal amount)
            => LedgerService.SpendFromSavedCash(userId, amount);

        public static void SpendFromEnvelope(int userId, int envelopeId, decimal amount)
            => LedgerService.SpendFromEnvelope(userId, envelopeId, amount);

        public static void SpendFromBankAccount(int userId, int accountId, decimal amount)
            => LedgerService.SpendFromBankAccount(userId, accountId, amount);

        // =========================
        //  Transfery – WSZYSTKIE kombinacje
        // =========================

        public static int TransferFreeToSaved(int userId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferFreeToSaved(userId, amount, date, desc);

        public static int TransferSavedToFree(int userId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferSavedToFree(userId, amount, date, desc);

        public static int TransferSavedToBank(int userId, int accountId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferSavedToBank(userId, accountId, amount, date, desc);

        public static int TransferBankToSaved(int userId, int accountId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferBankToSaved(userId, accountId, amount, date, desc);

        public static int TransferBankToBank(int userId, int fromAccountId, int toAccountId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferBankToBank(userId, fromAccountId, toAccountId, amount, date, desc);

        public static int TransferSavedToEnvelope(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferSavedToEnvelope(userId, envelopeId, amount, date, desc);

        public static int TransferEnvelopeToSaved(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferEnvelopeToSaved(userId, envelopeId, amount, date, desc);

        public static int TransferFreeToEnvelope(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferFreeToEnvelope(userId, envelopeId, amount, date, desc);

        public static int TransferEnvelopeToFree(int userId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferEnvelopeToFree(userId, envelopeId, amount, date, desc);

        public static int TransferEnvelopeToEnvelope(int userId, int fromEnvelopeId, int toEnvelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferEnvelopeToEnvelope(userId, fromEnvelopeId, toEnvelopeId, amount, date, desc);

        // =========================
        //  Kombinacje „czytelne” (atomowo: 1 transfer Any)
        // =========================

        public static void UpdatePlannedTransactionDate(LedgerService.TransactionSource src, int userId, int id, DateTime newDate)
        {
            switch (src)
            {
                case LedgerService.TransactionSource.Expense:
                    DatabaseService.UpdatePlannedExpenseDate(userId, id, newDate);
                    break;

                case LedgerService.TransactionSource.Income:
                    DatabaseService.UpdatePlannedIncomeDate(userId, id, newDate);
                    break;

                case LedgerService.TransactionSource.Transfer:
                    DatabaseService.UpdatePlannedTransferDate(userId, id, newDate);
                    break;

                default:
                    throw new InvalidOperationException("Nieobsługiwany typ transakcji.");
            }
        }

        public static int TransferBankToFreeCash(int userId, int accountId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferAny(
                userId, amount, date ?? DateTime.Today, desc,
                fromKind: "bank", fromRefId: accountId,
                toKind: "freecash", toRefId: null,
                isPlanned: false);

        public static int TransferFreeCashToBank(int userId, int accountId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferAny(
                userId, amount, date ?? DateTime.Today, desc,
                fromKind: "freecash", fromRefId: null,
                toKind: "bank", toRefId: accountId,
                isPlanned: false);

        public static int TransferEnvelopeToBank(int userId, int envelopeId, int accountId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferAny(
                userId, amount, date ?? DateTime.Today, desc,
                fromKind: "envelope", fromRefId: envelopeId,
                toKind: "bank", toRefId: accountId,
                isPlanned: false);

        public static int TransferBankToEnvelope(int userId, int accountId, int envelopeId, decimal amount, DateTime? date = null, string? desc = null)
            => LedgerService.TransferAny(
                userId, amount, date ?? DateTime.Today, desc,
                fromKind: "bank", fromRefId: accountId,
                toKind: "envelope", toRefId: envelopeId,
                isPlanned: false);

        public static void DeleteIncome(int incomeId)
        {
            LedgerService.DeleteIncome(incomeId);
        }

    }
}
