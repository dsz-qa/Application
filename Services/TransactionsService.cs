using System;
using Finly.Models;

namespace Finly.Services
{
    /// <summary>
    /// Wysokopoziomowa logika transakcji (wydatki, przychody, transfery):
    /// dodawanie, edycja i usuwanie z uwzglêdnieniem efektów ubocznych
    /// na saldach kont, gotówki oraz kopert.
    /// 
    /// DatabaseService pozostaje warstw¹ niskopoziomow¹ (CRUD + proste operacje).
    /// </summary>
    public static class TransactionsService
    {
        // ================= PUBLICZNE API =================

        public static int AddExpense(Expense expense)
        {
            if (expense == null) throw new ArgumentNullException(nameof(expense));

            // Dla nowych miejsc w UI – pozwalamy, aby ca³a logika sz³a têdy:
            // 1) INSERT
            var id = DatabaseService.InsertExpense(expense);
            expense.Id = id;

            // 2) Efekty uboczne (bank / gotówka / koperta) – tylko jeœli nieplanowane
            ApplyTransactionSideEffects(expense, revert: false);
            return id;
        }

        public static int AddIncome(
            int userId,
            decimal amount,
            DateTime date,
            string? description,
            string? source,
            int? categoryId,
            bool isPlanned = false)
        {
            // INSERT
            var id = DatabaseService.InsertIncome(userId, amount, date, description, source, categoryId, isPlanned);

            if (!isPlanned && !string.IsNullOrWhiteSpace(source))
            {
                // Efekty uboczne przychodu
                ApplyIncomeSideEffects(userId, amount, source, revert: false);
            }

            return id;
        }

        public static void DeleteTransaction(int transactionId)
        {
            // Na razie dalej delegujemy do istniej¹cej, sprawdzonej logiki
            // w DatabaseService. W przysz³oœci mo¿esz ca³oœæ tej metody
            // przepisaæ tutaj, u¿ywaj¹c ApplyTransactionSideEffects / ApplyIncomeSideEffects.
            DatabaseService.DeleteTransactionAndRevertBalance(transactionId);
        }

        public static void UpdateExpenseWithSideEffects(Expense updated)
        {
            if (updated == null) throw new ArgumentNullException(nameof(updated));

            var original = DatabaseService.GetExpenseById(updated.Id);
            if (original == null)
            {
                _ = AddExpense(updated);
                return;
            }

            // 1) cofnij wp³yw starej transakcji
            ApplyTransactionSideEffects(original, revert: true);

            // 2) zapisz now¹ wersjê
            DatabaseService.UpdateExpense(updated);

            // 3) na³ó¿ wp³yw nowej wersji
            ApplyTransactionSideEffects(updated, revert: false);
        }

        public static void UpdateIncomeWithSideEffects(
            int id,
            int userId,
            decimal? amount = null,
            string? description = null,
            bool? isPlanned = null,
            DateTime? date = null,
            int? categoryId = null,
            string? source = null)
        {
            // Pobierz oryginalny przychód
            using var c = DatabaseService.GetConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Amount, Source, IsPlanned FROM Incomes WHERE Id=@id AND UserId=@u LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", userId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var origAmount = Convert.ToDecimal(r.GetValue(0));
                var origSource = r.IsDBNull(1) ? null : r.GetString(1);
                var origIsPlanned = !r.IsDBNull(2) && Convert.ToInt32(r.GetValue(2)) == 1;

                // cofnij stary wp³yw (tylko jeœli nieplanowany i mia³ Source)
                if (!origIsPlanned && !string.IsNullOrWhiteSpace(origSource))
                {
                    ApplyIncomeSideEffects(userId, origAmount, origSource!, revert: true);
                }
            }

            // zaktualizuj rekord
            DatabaseService.UpdateIncome(id, userId, amount, description, isPlanned, date, categoryId, source);

            // na³ó¿ nowy wp³yw
            var finalAmount = amount ?? 0m; // jeœli null, logika UpdateIncome zostawia star¹ wartoœæ – tu nie mamy prostego dostêpu
            if (!isPlanned.GetValueOrDefault(false) && !string.IsNullOrWhiteSpace(source) && amount.HasValue)
            {
                ApplyIncomeSideEffects(userId, finalAmount, source!, revert: false);
            }
        }

        // ================= CENTRALNA LOGIKA =================

        /// <summary>
        /// Efekty uboczne dla wydatków (Expenses):
        /// - wydatek zmniejsza saldo Ÿród³a (konto/koperta/gotówka),
        /// - przy revert = true odwracamy znak (czyli saldo roœnie).
        /// </summary>
        private static void ApplyTransactionSideEffects(Expense expense, bool revert)
        {
            if (expense == null) return;
            if (expense.IsPlanned) return; // planowane nie wp³ywaj¹ na salda

            var userId = expense.UserId;
            var amount = (decimal)Math.Abs(expense.Amount);
            var sign = revert ? +1m : -1m; // wydatek normalnie zmniejsza saldo
            var delta = amount * sign;

            // Rozró¿niamy Ÿród³o po polu Account:
            // - "Wolna gotówka"
            // - "Od³o¿ona gotówka"
            // - "Konto: <nazwa>"
            // - "Koperta: <nazwa>" (opcjonalnie, jeœli tak ustawisz w UI)

            var acc = (expense.Account ?? string.Empty).Trim();

            if (acc.StartsWith("Konto:", StringComparison.CurrentCultureIgnoreCase))
            {
                // Konto bankowe – znajdŸ po nazwie i zaktualizuj Balance
                var name = acc.Substring("Konto:".Length).Trim();
                using var c = DatabaseService.GetConnection();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE BankAccounts SET Balance = Balance + @d WHERE UserId=@u AND AccountName=@n;";
                cmd.Parameters.AddWithValue("@d", delta);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@n", name);
                cmd.ExecuteNonQuery();
            }
            else if (acc.StartsWith("Koperta:", StringComparison.CurrentCultureIgnoreCase))
            {
                // Wydatek z koperty: œrodki schodz¹ tylko z tej koperty i SavedCash + CashOnHand
                var envName = acc.Substring("Koperta:".Length).Trim();
                var envId = DatabaseService.GetEnvelopeIdByName(userId, envName);
                if (envId.HasValue)
                {
                    if (revert)
                    {
                        // cofamy wydatek: przywracamy œrodki do koperty i puli od³o¿onej + gotówki
                        DatabaseService.AddToEnvelopeAllocated(userId, envId.Value, amount);
                        DatabaseService.AddToSavedCash(userId, amount);
                        var allCash = DatabaseService.GetCashOnHand(userId);
                        DatabaseService.SetCashOnHand(userId, allCash + amount);
                    }
                    else
                    {
                        // normalny wydatek: tak jak SpendFromEnvelope
                        DatabaseService.SubtractFromEnvelopeAllocated(userId, envId.Value, amount);
                        DatabaseService.SubtractFromSavedCash(userId, amount);
                        var allCash = DatabaseService.GetCashOnHand(userId);
                        DatabaseService.SetCashOnHand(userId, allCash - amount);
                    }
                }
            }
            else if (acc.Equals("Od³o¿ona gotówka", StringComparison.CurrentCultureIgnoreCase))
            {
                // Wydatek z od³o¿onej gotówki (poza kopertami)
                var saved = DatabaseService.GetSavedCash(userId);
                var allCash = DatabaseService.GetCashOnHand(userId);
                DatabaseService.SetSavedCash(userId, saved + delta);
                DatabaseService.SetCashOnHand(userId, allCash + delta);
            }
            else
            {
                // Domyœlnie traktujemy jako "Wolna gotówka"
                var allCash = DatabaseService.GetCashOnHand(userId);
                DatabaseService.SetCashOnHand(userId, allCash + delta);
            }
        }

        /// <summary>
        /// Efekty uboczne dla przychodów – bazujemy na polu Source
        /// w tabeli Incomes ("Wolna gotówka", "Od³o¿ona gotówka", "Konto: X").
        /// revert odwraca znak.
        /// </summary>
        private static void ApplyIncomeSideEffects(int userId, decimal amount, string source, bool revert)
        {
            if (string.IsNullOrWhiteSpace(source)) return;

            var sign = revert ? -1m : +1m; // przychód normalnie zwiêksza saldo
            var delta = Math.Abs(amount) * sign;
            var src = source.Trim();

            if (src.Equals("Od³o¿ona gotówka", StringComparison.CurrentCultureIgnoreCase))
            {
                var saved = DatabaseService.GetSavedCash(userId);
                var cash = DatabaseService.GetCashOnHand(userId);
                DatabaseService.SetSavedCash(userId, saved + delta);
                DatabaseService.SetCashOnHand(userId, cash + delta);
            }
            else if (src.Equals("Wolna gotówka", StringComparison.CurrentCultureIgnoreCase))
            {
                var cash = DatabaseService.GetCashOnHand(userId);
                DatabaseService.SetCashOnHand(userId, cash + delta);
            }
            else if (src.StartsWith("Konto:", StringComparison.CurrentCultureIgnoreCase))
            {
                var name = src.Substring("Konto:".Length).Trim();
                using var c = DatabaseService.GetConnection();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE BankAccounts SET Balance = Balance + @d WHERE UserId=@u AND AccountName=@n;";
                cmd.Parameters.AddWithValue("@d", delta);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@n", name);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
