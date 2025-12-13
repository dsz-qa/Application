using Microsoft.Data.Sqlite;
using System;
using System.Globalization;

namespace Finly.Services.Ledger
{
    /// <summary>
    /// Jedyny właściciel księgowania w aplikacji.
    /// Tu jest cała logika wpływu na salda (CashOnHand / SavedCash / Envelopes / BankAccounts)
    /// oraz odwracanie transakcji przy usuwaniu.
    /// </summary>
    public static class LedgerService
    {
        // ====== NARZĘDZIA / SPÓJNOŚĆ ======

        private static string ToIsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static void EnsureNonNegative(decimal amount, string paramName)
        {
            if (amount <= 0) throw new ArgumentException("Kwota musi być dodatnia.", paramName);
        }

        // ====== GET/SET gotówka (operacje niskopoziomowe) ======

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

        // ====== PUBLIC: OPERACJE KSIĘGOWANIA ======

        /// <summary>
        /// Wolna gotówka = CashOnHand - SavedCash. Zmniejsza tylko CashOnHand.
        /// </summary>
        public static void SpendFromFreeCash(int userId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();

            using var tx = c.BeginTransaction();

            var allCash = GetCashOnHand(c, tx, userId);
            var saved = GetSavedCash(c, tx, userId);
            var free = Math.Max(0m, allCash - saved);

            if (free < amount) throw new InvalidOperationException("Za mało wolnej gotówki na taki wydatek.");

            SubCash(c, tx, userId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromSavedCash(int userId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // wydanie z odłożonej: zmniejsza SavedCash i CashOnHand
            SubSaved(c, tx, userId, amount);
            SubCash(c, tx, userId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromEnvelope(int userId, int envelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // koperta to podzbiór SavedCash:
            SubEnvelopeAllocated(c, tx, userId, envelopeId, amount);
            SubSaved(c, tx, userId, amount);
            SubCash(c, tx, userId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void SpendFromBankAccount(int userId, int accountId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            SubBank(c, tx, userId, accountId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        // ====== TRANSFERY PUL (cash/saved/envelopes/bank) ======

        public static void TransferFreeToSaved(int userId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            var allCash = GetCashOnHand(c, tx, userId);
            var saved = GetSavedCash(c, tx, userId);
            var free = Math.Max(0m, allCash - saved);

            if (free < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");

            AddSaved(c, tx, userId, amount);
            // CashOnHand bez zmian, bo to tylko podział puli

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferSavedToFree(int userId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            var saved = GetSavedCash(c, tx, userId);
            if (saved < amount) throw new InvalidOperationException("Za mało odłożonej gotówki.");

            SubSaved(c, tx, userId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferSavedToBank(int userId, int accountId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // wypływ z saved i z portfela, wpływ na bank
            SubSaved(c, tx, userId, amount);
            SubCash(c, tx, userId, amount);
            AddBank(c, tx, userId, accountId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferBankToSaved(int userId, int accountId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // wypływ z banku, wpływ do portfela i saved
            SubBank(c, tx, userId, accountId, amount);
            AddCash(c, tx, userId, amount);
            AddSaved(c, tx, userId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferBankToBank(int userId, int fromAccountId, int toAccountId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            if (fromAccountId == toAccountId) return;

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            SubBank(c, tx, userId, fromAccountId, amount);
            AddBank(c, tx, userId, toAccountId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferSavedToEnvelope(int userId, int envelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            var saved = GetSavedCash(c, tx, userId);
            // suma kopert może być policzona z DB – na razie minimalnie: dopuszczamy tylko jeśli saved >= amount + currentAllocated? (bezpiecznie)
            // Prosty wariant: nie pozwalaj alokować więcej niż SavedCash "wolne".
            decimal allocatedSum;
            using (var sum = c.CreateCommand())
            {
                sum.Transaction = tx;
                sum.CommandText = "SELECT COALESCE(SUM(Allocated),0) FROM Envelopes WHERE UserId=@u;";
                sum.Parameters.AddWithValue("@u", userId);
                allocatedSum = Convert.ToDecimal(sum.ExecuteScalar() ?? 0m);
            }

            var unassigned = saved - allocatedSum;
            if (unassigned < amount) throw new InvalidOperationException("Za mało odłożonej gotówki poza kopertami.");

            AddEnvelopeAllocated(c, tx, userId, envelopeId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferEnvelopeToEnvelope(int userId, int fromEnvelopeId, int toEnvelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));
            if (fromEnvelopeId == toEnvelopeId) return;

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            SubEnvelopeAllocated(c, tx, userId, fromEnvelopeId, amount);
            AddEnvelopeAllocated(c, tx, userId, toEnvelopeId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }


        public static void TransferEnvelopeToSaved(int userId, int envelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // to tylko zmniejsza Allocated – SavedCash bez zmian
            SubEnvelopeAllocated(c, tx, userId, envelopeId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferFreeToEnvelope(int userId, int envelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            var allCash = GetCashOnHand(c, tx, userId);
            var saved = GetSavedCash(c, tx, userId);
            var free = Math.Max(0m, allCash - saved);
            if (free < amount) throw new InvalidOperationException("Za mało wolnej gotówki.");

            // wolna -> koperta oznacza: SavedCash rośnie + alokacja rośnie, CashOnHand bez zmian
            AddSaved(c, tx, userId, amount);
            AddEnvelopeAllocated(c, tx, userId, envelopeId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        public static void TransferEnvelopeToFree(int userId, int envelopeId, decimal amount)
        {
            EnsureNonNegative(amount, nameof(amount));

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // koperta -> wolna: Allocated maleje, SavedCash maleje, CashOnHand bez zmian
            SubEnvelopeAllocated(c, tx, userId, envelopeId, amount);
            SubSaved(c, tx, userId, amount);

            tx.Commit();
            DatabaseService.NotifyDataChanged();
        }

        // ====== USUWANIE I ODWRACANIE ======

        /// <summary>
        /// Usuwa transakcję (Transfer/Expense/Income) o danym Id i odwraca jej wpływ na saldo.
        /// To jest "jedyny pewny" punkt kasowania w appce.
        /// </summary>
        public static void DeleteTransactionAndRevertBalance(int transactionId)
        {
            if (transactionId <= 0) return;

            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            // 1) TRANSFERS
            if (DatabaseService.TableExists(c, "Transfers"))
            {
                using var get = c.CreateCommand();
                get.Transaction = tx;
                get.CommandText = @"SELECT UserId, Amount, FromKind, FromRefId, ToKind, ToRefId FROM Transfers WHERE Id=@id LIMIT 1;";
                get.Parameters.AddWithValue("@id", transactionId);

                using var r = get.ExecuteReader();
                if (r.Read())
                {
                    var userId = r.GetInt32(0);
                    var amount = Convert.ToDecimal(r.GetValue(1));
                    var fromKind = (r.IsDBNull(2) ? "" : r.GetString(2)).Trim().ToLowerInvariant();
                    var fromRef = r.IsDBNull(3) ? (int?)null : r.GetInt32(3);
                    var toKind = (r.IsDBNull(4) ? "" : r.GetString(4)).Trim().ToLowerInvariant();
                    var toRef = r.IsDBNull(5) ? (int?)null : r.GetInt32(5);

                    // Odwracamy:
                    // bank->bank: +from, -to
                    if (fromKind == "bank" && toKind == "bank")
                    {
                        if (fromRef.HasValue) AddBank(c, tx, userId, fromRef.Value, amount);
                        if (toRef.HasValue) SubBank(c, tx, userId, toRef.Value, amount);
                    }
                    // bank->cash: +bank, -cash
                    else if (fromKind == "bank" && toKind == "cash")
                    {
                        if (fromRef.HasValue) AddBank(c, tx, userId, fromRef.Value, amount);
                        SubCash(c, tx, userId, amount);
                    }
                    // cash->bank: +cash, -bank
                    else if (fromKind == "cash" && toKind == "bank")
                    {
                        AddCash(c, tx, userId, amount);
                        if (toRef.HasValue) SubBank(c, tx, userId, toRef.Value, amount);
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

            // 2) EXPENSES
            if (DatabaseService.TableExists(c, "Expenses"))
            {
                bool hasAccId = DatabaseService.ColumnExists(c, "Expenses", "AccountId");
                bool hasLegacyAccText = DatabaseService.ColumnExists(c, "Expenses", "Account");

                using var get = c.CreateCommand();
                get.Transaction = tx;
                get.CommandText = hasAccId
                    ? @"SELECT UserId, Amount, AccountId, IsPlanned FROM Expenses WHERE Id=@id LIMIT 1;"
                    : @"SELECT UserId, Amount, NULL as AccountId, IsPlanned FROM Expenses WHERE Id=@id LIMIT 1;";
                get.Parameters.AddWithValue("@id", transactionId);

                using var r = get.ExecuteReader();
                if (r.Read())
                {
                    var userId = r.GetInt32(0);
                    var amount = Convert.ToDecimal(r.GetValue(1));
                    int? accountId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
                    var isPlanned = !r.IsDBNull(3) && Convert.ToInt32(r.GetValue(3)) == 1;

                    // legacy mapping (opcjonalnie)
                    if (!accountId.HasValue && hasLegacyAccText)
                    {
                        using var cmdTxt = c.CreateCommand();
                        cmdTxt.Transaction = tx;
                        cmdTxt.CommandText = "SELECT Account FROM Expenses WHERE Id=@id LIMIT 1;";
                        cmdTxt.Parameters.AddWithValue("@id", transactionId);
                        var txt = cmdTxt.ExecuteScalar()?.ToString();
                        accountId = TryResolveAccountIdFromExpenseText(c, tx, userId, txt);
                    }

                    // odwracamy tylko jeśli zrealizowany
                    if (!isPlanned)
                    {
                        if (accountId.HasValue)
                            AddBank(c, tx, userId, accountId.Value, amount);
                        else
                            AddCash(c, tx, userId, amount);
                    }

                    using var del = c.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM Expenses WHERE Id=@id;";
                    del.Parameters.AddWithValue("@id", transactionId);
                    del.ExecuteNonQuery();

                    tx.Commit();
                    DatabaseService.NotifyDataChanged();
                    return;
                }
            }

            // 3) INCOMES
            if (DatabaseService.TableExists(c, "Incomes"))
            {
                using var get = c.CreateCommand();
                get.Transaction = tx;
                get.CommandText = @"SELECT UserId, Amount, Source, IsPlanned FROM Incomes WHERE Id=@id LIMIT 1;";
                get.Parameters.AddWithValue("@id", transactionId);

                using var r = get.ExecuteReader();
                if (r.Read())
                {
                    var userId = r.GetInt32(0);
                    var amount = Convert.ToDecimal(r.GetValue(1));
                    var source = r.IsDBNull(2) ? "" : (r.GetString(2) ?? "");
                    var isPlanned = !r.IsDBNull(3) && Convert.ToInt32(r.GetValue(3)) == 1;

                    if (!isPlanned)
                    {
                        // Cofamy wpływ wg źródła (Twoja dotychczasowa konwencja):
                        var src = (source ?? "").Trim();
                        if (src.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
                        {
                            var name = src.Substring("Konto:".Length).Trim();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                using var sub = c.CreateCommand();
                                sub.Transaction = tx;
                                sub.CommandText = @"UPDATE BankAccounts SET Balance = Balance - @a WHERE UserId=@u AND AccountName=@n;";
                                sub.Parameters.AddWithValue("@a", amount);
                                sub.Parameters.AddWithValue("@u", userId);
                                sub.Parameters.AddWithValue("@n", name);
                                sub.ExecuteNonQuery();
                            }
                        }
                        else if (src.Equals("Wolna gotówka", StringComparison.OrdinalIgnoreCase))
                        {
                            SubCash(c, tx, userId, amount);
                        }
                        else if (src.Equals("Odłożona gotówka", StringComparison.OrdinalIgnoreCase))
                        {
                            SubSaved(c, tx, userId, amount);
                            SubCash(c, tx, userId, amount);
                        }
                    }

                    using var del = c.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM Incomes WHERE Id=@id;";
                    del.Parameters.AddWithValue("@id", transactionId);
                    del.ExecuteNonQuery();

                    tx.Commit();
                    DatabaseService.NotifyDataChanged();
                    return;
                }
            }

            // nic nie znaleziono
            tx.Rollback();
        }

        private static int? TryResolveAccountIdFromExpenseText(SqliteConnection c, SqliteTransaction tx, int userId, string? accountText)
        {
            if (string.IsNullOrWhiteSpace(accountText)) return null;

            var t = accountText.Trim();
            if (t.StartsWith("Konto:", StringComparison.OrdinalIgnoreCase))
            {
                var name = t.Substring("Konto:".Length).Trim();
                if (string.IsNullOrWhiteSpace(name)) return null;

                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"SELECT Id FROM BankAccounts WHERE UserId=@u AND AccountName=@n LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@n", name);
                var obj = cmd.ExecuteScalar();
                return (obj == null || obj == DBNull.Value) ? (int?)null : Convert.ToInt32(obj);
            }

            return null;
        }
    }
}

