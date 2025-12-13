using System;
using Finly.Services.Ledger;

namespace Finly.Services
{
    /// <summary>
    /// FASADA dla UI – jedno miejsce do operacji na transakcjach/saldach.
    /// UI nie powinno wołać DatabaseService do zmiany sald.
    /// </summary>
    public static class TransactionsFacadeService
    {

        public static void SetCashOnHand(int userId, decimal amount)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE CashOnHand SET Amount = @a WHERE UserId = @u;
INSERT INTO CashOnHand(UserId, Amount)
SELECT @u, @a
WHERE (SELECT changes()) = 0;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();
        }

        public static void SetSavedCash(int userId, decimal amount)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE SavedCash SET Amount = @a WHERE UserId = @u;
INSERT INTO SavedCash(UserId, Amount)
SELECT @u, @a
WHERE (SELECT changes()) = 0;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.ExecuteNonQuery();
        }

        // ===== Kasowanie transakcji z odwróceniem skutków =====
        public static void DeleteTransaction(int id)
            => LedgerService.DeleteTransactionAndRevertBalance(id);

        // ===== Wydatki (księgowanie) =====
        public static void SpendFromFreeCash(int userId, decimal amount)
            => LedgerService.SpendFromFreeCash(userId, amount);

        public static void SpendFromSavedCash(int userId, decimal amount)
            => LedgerService.SpendFromSavedCash(userId, amount);

        public static void SpendFromEnvelope(int userId, int envelopeId, decimal amount)
            => LedgerService.SpendFromEnvelope(userId, envelopeId, amount);

        public static void SpendFromBankAccount(int userId, int accountId, decimal amount)
            => LedgerService.SpendFromBankAccount(userId, accountId, amount);

        // ===== Transfery (podstawowe) =====
        public static void TransferFreeToSaved(int userId, decimal amount)
            => LedgerService.TransferFreeToSaved(userId, amount);

        public static void TransferSavedToFree(int userId, decimal amount)
            => LedgerService.TransferSavedToFree(userId, amount);

        public static void TransferSavedToBank(int userId, int accountId, decimal amount)
            => LedgerService.TransferSavedToBank(userId, accountId, amount);

        public static void TransferBankToSaved(int userId, int accountId, decimal amount)
            => LedgerService.TransferBankToSaved(userId, accountId, amount);

        public static void TransferBankToBank(int userId, int fromAccountId, int toAccountId, decimal amount)
            => LedgerService.TransferBankToBank(userId, fromAccountId, toAccountId, amount);

        public static void TransferSavedToEnvelope(int userId, int envelopeId, decimal amount)
            => LedgerService.TransferSavedToEnvelope(userId, envelopeId, amount);

        public static void TransferEnvelopeToSaved(int userId, int envelopeId, decimal amount)
            => LedgerService.TransferEnvelopeToSaved(userId, envelopeId, amount);

        public static void TransferFreeToEnvelope(int userId, int envelopeId, decimal amount)
            => LedgerService.TransferFreeToEnvelope(userId, envelopeId, amount);

        public static void TransferEnvelopeToFree(int userId, int envelopeId, decimal amount)
            => LedgerService.TransferEnvelopeToFree(userId, envelopeId, amount);

        // ===== Transfery „czytelne” pod UI (kompozycje) =====

        /// <summary>
        /// Bank -> wolna gotówka (CashOnHand rośnie, SavedCash bez zmian).
        /// Realizacja: Bank -> Saved, potem Saved -> Free.
        /// </summary>
        public static void TransferBankToFreeCash(int userId, int accountId, decimal amount)
        {
            LedgerService.TransferBankToSaved(userId, accountId, amount);
            LedgerService.TransferSavedToFree(userId, amount);
        }

        /// <summary>
        /// Wolna gotówka -> Bank (CashOnHand maleje, SavedCash bez zmian).
        /// Realizacja: Free -> Saved, potem Saved -> Bank.
        /// </summary>
        public static void TransferFreeCashToBank(int userId, int accountId, decimal amount)
        {
            LedgerService.TransferFreeToSaved(userId, amount);
            LedgerService.TransferSavedToBank(userId, accountId, amount);
        }

        /// <summary>
        /// Koperta -> Konto bankowe. Realizacja: Envelope -> Saved, potem Saved -> Bank.
        /// </summary>
        public static void TransferEnvelopeToBank(int userId, int envelopeId, int accountId, decimal amount)
        {
            LedgerService.TransferEnvelopeToSaved(userId, envelopeId, amount);
            LedgerService.TransferSavedToBank(userId, accountId, amount);
        }

        /// <summary>
        /// Konto bankowe -> Koperta. Realizacja: Bank -> Saved, potem Saved -> Envelope.
        /// </summary>
        public static void TransferBankToEnvelope(int userId, int accountId, int envelopeId, decimal amount)
        {
            LedgerService.TransferBankToSaved(userId, accountId, amount);
            LedgerService.TransferSavedToEnvelope(userId, envelopeId, amount);
        }

        /// <summary>
        /// Koperta -> Koperta (alokacje). SavedCash bez zmian.
        /// </summary>
        public static void TransferEnvelopeToEnvelope(int userId, int fromEnvelopeId, int toEnvelopeId, decimal amount)
            => LedgerService.TransferEnvelopeToEnvelope(userId, fromEnvelopeId, toEnvelopeId, amount);
    }


}
