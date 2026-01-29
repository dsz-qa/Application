using Finly.Models;
using Microsoft.Data.Sqlite;
using System;

namespace Finly.Services.Features
{
    /// <summary>
    /// FASADA dla UI – jedno miejsce do operacji na transakcjach/saldach.
    /// UI nie powinno wołać DatabaseService do zmiany sald (poza SET-ami startowymi).
    /// </summary>
    public static class TransactionsFacadeService
    {
        // =========================
        //  SET (ustawienia sald)
        // =========================
        public static void SpendExpense(Expense e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (e.UserId <= 0) throw new InvalidOperationException("Brak UserId.");
            if (e.Amount <= 0) throw new InvalidOperationException("Kwota musi być > 0.");

            // 1) Zawsze zapisujemy rekord wydatku do DB.
            // Planned: bez wpływu na salda.
            DatabaseService.InsertExpense(e);

            if (e.IsPlanned)
                return;

            // 2) Real: księgowanie wyłącznie przez LedgerService (bez parametrów których nie ma).
            var amount = (decimal)e.Amount;

            switch (e.PaymentKind)
            {
                case PaymentKind.FreeCash:
                    LedgerService.SpendFromFreeCash(e.UserId, amount);
                    break;

                case PaymentKind.SavedCash:
                    LedgerService.SpendFromSavedCash(e.UserId, amount);
                    break;

                case PaymentKind.BankAccount:
                    {
                        if (e.PaymentRefId is not int bankId || bankId <= 0)
                            throw new InvalidOperationException("Brak BankAccountId w PaymentRefId.");

                        LedgerService.SpendFromBankAccount(e.UserId, bankId, amount);
                        break;
                    }

                case PaymentKind.Envelope:
                    {
                        if (e.PaymentRefId is not int envId || envId <= 0)
                            throw new InvalidOperationException("Brak EnvelopeId w PaymentRefId.");

                        LedgerService.SpendFromEnvelope(e.UserId, envId, amount);
                        break;
                    }

                default:
                    throw new InvalidOperationException("Nieznany PaymentKind.");
            }
        }

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
SET Amount    = excluded.Amount,
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
SET Amount    = excluded.Amount,
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

        public static void DeleteTransaction(LedgerService.TransactionSource src, int id)
            => LedgerService.DeleteTransactionAndRevertBalance(src, id);

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
        //  Planned date update (UI)
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

        // =========================
        //  Raty kredytów
        // =========================

        /// <summary>
        /// Realizuje planned Expense (np. rata kredytu): planned -> realized i oznacza ratę jako Paid.
        /// Bezpiecznie: nie wywracamy appki, nie zakładamy istnienia metod w LedgerService które mogą nie istnieć.
        /// </summary>
        public static void SpendPlannedExpense(int userId, int expenseId, DateTime realizedDate)
        {
            if (userId <= 0) return;
            if (expenseId <= 0) return;

            var exp = DatabaseService.GetExpenseById(userId, expenseId);
            if (exp == null) return;

            if (!exp.IsPlanned)
                return;

            exp.IsPlanned = false;
            exp.Date = realizedDate;

            // Stabilnie: zapis przez DatabaseService (zależnie od Twojego projektu)
            DatabaseService.UpdateExpense(exp);

            // HOOK: oznacz ratę jako PAID
            if (exp.LoanInstallmentId.HasValue && exp.LoanInstallmentId.Value > 0)
            {
                DatabaseService.MarkLoanInstallmentAsPaidFromExpense(
                    userId: exp.UserId,
                    loanInstallmentId: exp.LoanInstallmentId.Value,
                    paidAt: exp.Date,
                    paymentKind: (int)exp.PaymentKind,
                    paymentRefId: exp.PaymentRefId
                );
            }

            DatabaseService.NotifyDataChanged();
        }

        // =========================
        //  Extra kombinacje Any
        // =========================

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
            => LedgerService.DeleteIncome(incomeId);
    }
}
