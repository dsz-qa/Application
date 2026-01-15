using Finly.Models;
using Finly.Services.Features;
using Finly.Views.Dialogs;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Finly.Services.SpecificPages
{
    public static class BudgetService
    {
        // =========================
        //  MODELE DTO DLA SERWISU
        // =========================

        public class BudgetOverAlert
        {
            public int BudgetId { get; set; }
            public string Name { get; set; } = "";
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }

            public decimal PlannedAmount { get; set; }
            public decimal Spent { get; set; }
            public decimal Incomes { get; set; }

            public decimal Remaining => PlannedAmount + Incomes - Spent;
            public decimal OverAmount => Remaining < 0 ? Math.Abs(Remaining) : 0m;

            public string Period => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}";
        }

        public class BudgetSummary
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            /// <summary>
            /// Weekly / Monthly / Yearly / Custom
            /// </summary>
            public string Type { get; set; } = "";
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public decimal PlannedAmount { get; set; }
            public decimal Spent { get; set; }
            public decimal IncomesForBudget { get; set; }

            public decimal Remaining => PlannedAmount + IncomesForBudget - Spent;

            public decimal UsedPercent =>
                PlannedAmount + IncomesForBudget == 0
                    ? 0
                    : Spent / (PlannedAmount + IncomesForBudget) * 100m;

            public string Period => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}";
        }

        // NOWE: transakcje budżetu do prawej listy
        public sealed class BudgetTransactionRow
        {
            public DateTime Date { get; set; }

            // typ transakcji w UI
            public bool IsIncome { get; set; }
            public bool IsTransfer { get; set; } // na razie raczej false
            public bool IsPlanned { get; set; }

            public decimal Amount { get; set; }
            public string? Description { get; set; }

            // UI meta
            public string? CategoryName { get; set; }
            public string? FromAccountName { get; set; }

            // helper dla XAML: pokazuj "Z konta" dla wydatku / transferu
            public bool ShowFromAccount => !IsIncome; // wydatek lub transfer

            public string DateText => Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            public string AmountText => $"{Amount:N2} zł";
        }


        // =========================
        //  CRUD / QUERY
        // =========================

        private static bool ColumnExists(SqliteConnection con, string table, string column)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1);
                if (name.Equals(column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string? ResolvePaymentName(SqliteConnection con, string? paymentKind, long? paymentRefId)
        {
            if (string.IsNullOrWhiteSpace(paymentKind) || paymentRefId == null || paymentRefId <= 0)
                return null;

            // Uwaga: dopasuj stringi do Twoich realnych wartości PaymentKind w DB.
            // Jeżeli u Ciebie PaymentKind jest np. "BankAccount"/"Envelope"/"CashOnHand" itp., to zadziała.
            // Jeśli inne – podmień mapowanie na swoje.

            var kind = paymentKind.Trim();

            try
            {
                if (kind.Equals("BankAccount", StringComparison.OrdinalIgnoreCase))
                {
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "SELECT Name FROM BankAccounts WHERE Id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@id", paymentRefId.Value);
                    return cmd.ExecuteScalar() as string;
                }

                if (kind.Equals("Envelope", StringComparison.OrdinalIgnoreCase))
                {
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "SELECT Name FROM Envelopes WHERE Id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@id", paymentRefId.Value);
                    return cmd.ExecuteScalar() as string;
                }

                if (kind.Equals("CashOnHand", StringComparison.OrdinalIgnoreCase))
                    return "Gotówka";

                if (kind.Equals("SavedCash", StringComparison.OrdinalIgnoreCase))
                    return "Oszczędności (gotówka)";
            }
            catch
            {
                // nie wywalaj UI jeśli tabela/kolumny nie istnieją
            }

            return null;
        }


        public static IList<Budget> GetBudgetsForUser(int userId, DateTime? from = null, DateTime? to = null)
        {
            var result = new List<Budget>();

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, UserId, Name, Type, StartDate, EndDate, PlannedAmount
FROM Budgets
WHERE UserId = @uid
  AND IFNULL(IsDeleted,0) = 0
";

            if (from.HasValue)
                cmd.CommandText += " AND date(EndDate) >= date(@from)";
            if (to.HasValue)
                cmd.CommandText += " AND date(StartDate) <= date(@to)";

            cmd.Parameters.AddWithValue("@uid", userId);
            if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
            if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new Budget
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Type = reader.IsDBNull(3) ? "Monthly" : reader.GetString(3),
                    StartDate = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    EndDate = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    PlannedAmount = ReadDecimal(reader, 6)
                });
            }

            return result;
        }

        public static int InsertBudget(int userId, BudgetDialogViewModel vm)
        {
            using var con = DatabaseService.GetConnection();
            con.Open();

            var type = (vm.Type ?? "Monthly").Trim();
            if (string.IsNullOrWhiteSpace(type)) type = "Monthly";

            if (type.Equals("Inny", StringComparison.OrdinalIgnoreCase)) type = "Custom";

            var start = (vm.StartDate ?? DateTime.Today).Date;
            var end = (vm.EndDate ?? start).Date;

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Budgets (UserId, Name, Type, StartDate, EndDate, PlannedAmount)
VALUES (@uid, @name, @type, @start, @end, @planned);
SELECT last_insert_rowid();
";

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@name", vm.Name ?? "");
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@start", start.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@end", end.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@planned", (double)vm.PlannedAmount);

            var id = (long)cmd.ExecuteScalar();
            return (int)id;
        }

        public static void UpdateBudget(Budget b)
        {
            using var conn = DatabaseService.GetConnection();
            conn.Open();

            var type = (b.Type ?? "Monthly").Trim();
            if (string.IsNullOrWhiteSpace(type)) type = "Monthly";
            if (type.Equals("Inny", StringComparison.OrdinalIgnoreCase)) type = "Custom";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE Budgets
SET Name = @name,
    Type = @type,
    StartDate = @start,
    EndDate = @end,
    PlannedAmount = @planned
WHERE Id = @id AND UserId = @uid;
";

            cmd.Parameters.AddWithValue("@id", b.Id);
            cmd.Parameters.AddWithValue("@uid", b.UserId);
            cmd.Parameters.AddWithValue("@name", b.Name ?? "");
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@start", b.StartDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@end", b.EndDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@planned", (double)b.PlannedAmount);

            cmd.ExecuteNonQuery();
        }

        public static void DeleteBudget(int id, int userId)
        {
            using var conn = DatabaseService.GetConnection();
            conn.Open();

            // Preferuj soft delete jeśli istnieje IsDeleted
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE Budgets SET IsDeleted = 1
WHERE Id = @id AND UserId = @uid;
";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@uid", userId);

            var affected = cmd.ExecuteNonQuery();
            if (affected == 0)
            {
                // awaryjnie twarde kasowanie (gdy stara baza bez IsDeleted)
                using var hard = conn.CreateCommand();
                hard.CommandText = "DELETE FROM Budgets WHERE Id = @id AND UserId = @uid;";
                hard.Parameters.AddWithValue("@id", id);
                hard.Parameters.AddWithValue("@uid", userId);
                hard.ExecuteNonQuery();
            }
        }

        // =========================
        //  PODSUMOWANIA / ALERTY
        // =========================

        public static List<BudgetSummary> GetBudgetsWithSummary(int userId)
        {
            var result = new List<BudgetSummary>();

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();

            // WAŻNE: nie robimy JOIN Expenses + JOIN Incomes, bo to mnoży wiersze i psuje SUM()
            // Zamiast tego: subquery per budżet, dodatkowo ograniczenie do okresu budżetu.
            cmd.CommandText = @"
SELECT
    b.Id,
    b.Name,
    b.Type,
    b.StartDate,
    b.EndDate,
    b.PlannedAmount,
    IFNULL((
        SELECT SUM(e.Amount)
        FROM Expenses e
        WHERE e.UserId = b.UserId
          AND e.BudgetId = b.Id
          AND date(e.Date) BETWEEN date(b.StartDate) AND date(b.EndDate)
    ), 0) AS Spent,
    IFNULL((
        SELECT SUM(i.Amount)
        FROM Incomes i
        WHERE i.UserId = b.UserId
          AND i.BudgetId = b.Id
          AND date(i.Date) BETWEEN date(b.StartDate) AND date(b.EndDate)
    ), 0) AS IncomesForBudget
FROM Budgets b
WHERE b.UserId = @uid
  AND IFNULL(b.IsDeleted,0) = 0
ORDER BY date(b.StartDate);
";

            cmd.Parameters.AddWithValue("@uid", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var item = new BudgetSummary
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Type = reader.IsDBNull(2) ? "Monthly" : reader.GetString(2),
                    StartDate = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    EndDate = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    PlannedAmount = ReadDecimal(reader, 5),
                    Spent = ReadDecimal(reader, 6),
                    IncomesForBudget = ReadDecimal(reader, 7)
                };

                if (item.Type.Equals("Inny", StringComparison.OrdinalIgnoreCase))
                    item.Type = "Custom";

                result.Add(item);
            }

            return result;
        }

        public static List<BudgetOverAlert> GetOverBudgetAlerts(int userId, DateTime rangeFrom, DateTime rangeTo)
        {
            var result = new List<BudgetOverAlert>();

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, Type, StartDate, EndDate, PlannedAmount
FROM Budgets
WHERE UserId = @uid
  AND IFNULL(IsDeleted,0) = 0
  AND date(EndDate) >= date(@from)
  AND date(StartDate) <= date(@to)
ORDER BY date(StartDate);
";

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@from", rangeFrom.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", rangeTo.ToString("yyyy-MM-dd"));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt32(0);
                var name = r.GetString(1);
                var start = DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture);
                var end = DateTime.Parse(r.GetString(4), CultureInfo.InvariantCulture);
                var planned = ReadDecimal(r, 5);

                // UWAGA: alerty liczymy w danym range, ale transakcje i tak filtrujemy na budżet+range
                var spent = SumExpensesForBudget(conn, userId, id, rangeFrom, rangeTo);
                var inc = SumIncomesForBudget(conn, userId, id, rangeFrom, rangeTo);

                var alert = new BudgetOverAlert
                {
                    BudgetId = id,
                    Name = name,
                    StartDate = start,
                    EndDate = end,
                    PlannedAmount = planned,
                    Spent = spent,
                    Incomes = inc
                };

                if (alert.OverAmount > 0m)
                    result.Add(alert);
            }

            return result;
        }

        // =========================
        //  TRANSAKCJE BUDŻETU (PRAWA LISTA)
        // =========================

        public static List<BudgetTransactionRow> GetBudgetTransactions(int userId, int budgetId, DateTime startDate, DateTime endDate)
        {
            var rows = new List<BudgetTransactionRow>();

            using var con = DatabaseService.GetConnection();
            con.Open();

            // Expenses
            // Expenses
            var hasPaymentKind = ColumnExists(con, "Expenses", "PaymentKind");
            var hasPaymentRefId = ColumnExists(con, "Expenses", "PaymentRefId");

            using (var cmd = con.CreateCommand())
            {
                // budujemy SELECT dynamicznie, żeby nie wysypać się na brakujących kolumnach
                cmd.CommandText = @"
SELECT
    e.Date,
    e.Amount,
    COALESCE(e.Title, e.Description, '') AS DescText,
    COALESCE(c.Name, '') AS CategoryName,
    COALESCE(e.IsPlanned,0) AS IsPlanned"
                + (hasPaymentKind ? ", e.PaymentKind AS PaymentKind" : "")
                + (hasPaymentRefId ? ", e.PaymentRefId AS PaymentRefId" : "")
                + @"
FROM Expenses e
LEFT JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId = @u
  AND e.BudgetId = @b
  AND date(e.Date) BETWEEN date(@from) AND date(@to)
ORDER BY date(e.Date) DESC, e.Id DESC;";

                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@b", budgetId);
                cmd.Parameters.AddWithValue("@from", startDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", endDate.ToString("yyyy-MM-dd"));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var date = ParseDate(r.GetString(0));
                    var amount = ReadDecimal(r, 1);
                    var desc = r.IsDBNull(2) ? null : r.GetString(2);
                    var cat = r.IsDBNull(3) ? null : r.GetString(3);
                    var planned = !r.IsDBNull(4) && Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture) == 1;

                    string? paymentKind = null;
                    long? paymentRefId = null;

                    var ord = 5;

                    if (hasPaymentKind)
                    {
                        paymentKind = r.IsDBNull(ord) ? null : r.GetString(ord);
                        ord++;
                    }
                    if (hasPaymentRefId)
                    {
                        if (!r.IsDBNull(ord))
                            paymentRefId = Convert.ToInt64(r.GetValue(ord), CultureInfo.InvariantCulture);
                    }

                    var fromName = ResolvePaymentName(con, paymentKind, paymentRefId);

                    rows.Add(new BudgetTransactionRow
                    {
                        Date = date,
                        IsIncome = false,
                        IsTransfer = false,
                        Amount = amount,
                        Description = desc,
                        CategoryName = string.IsNullOrWhiteSpace(cat) ? null : cat,
                        FromAccountName = fromName,
                        IsPlanned = planned
                    });
                }
            }


            // Incomes
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    i.Date,
    i.Amount,
    COALESCE(i.Description,'') AS DescText,
    COALESCE(i.Source,'') AS SourceText,
    COALESCE(i.IsPlanned,0) AS IsPlanned
FROM Incomes i
WHERE i.UserId = @u
  AND i.BudgetId = @b
  AND date(i.Date) BETWEEN date(@from) AND date(@to)
ORDER BY date(i.Date) DESC, i.Id DESC;
";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@b", budgetId);
                cmd.Parameters.AddWithValue("@from", startDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", endDate.ToString("yyyy-MM-dd"));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var date = ParseDate(r.GetString(0));
                    var amount = ReadDecimal(r, 1);
                    var desc = r.IsDBNull(2) ? null : r.GetString(2);
                    var src = r.IsDBNull(3) ? null : r.GetString(3);
                    var planned = !r.IsDBNull(4) && Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture) == 1;

                    rows.Add(new BudgetTransactionRow
                    {
                        Date = date,
                        IsIncome = true,
                        IsTransfer = false,
                        Amount = amount,
                        Description = desc,
                        CategoryName = string.IsNullOrWhiteSpace(src) ? null : src, // jeżeli chcesz, możesz to przenieść do osobnego pola
                        FromAccountName = null,
                        IsPlanned = planned
                    });
                }
            }
            rows.Sort((a, b) => b.Date.CompareTo(a.Date));
            return rows;
        }
        


        // =========================
        //  Helpers
        // =========================

        private static DateTime ParseDate(string s)
        {
            // Najczęściej zapisujesz yyyy-MM-dd. Ale asekuracja na stare formaty.
            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;
            return DateTime.Today;
        }

        private static decimal ReadDecimal(System.Data.IDataRecord r, int ordinal)
        {
            if (r.IsDBNull(ordinal)) return 0m;

            var obj = r.GetValue(ordinal);
            return obj switch
            {
                decimal d => d,
                double dbl => Convert.ToDecimal(dbl, CultureInfo.InvariantCulture),
                float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
                long l => l,
                int i => i,
                string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) => v,
                _ => Convert.ToDecimal(obj, CultureInfo.InvariantCulture)
            };
        }

        private static decimal SumExpensesForBudget(SqliteConnection conn, int userId, int budgetId, DateTime from, DateTime to)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT IFNULL(SUM(Amount), 0)
FROM Expenses
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) BETWEEN date(@from) AND date(@to);
";
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@bid", budgetId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

            var val = cmd.ExecuteScalar();
            return val == null || val == DBNull.Value ? 0m : Convert.ToDecimal(val, CultureInfo.InvariantCulture);
        }

        private static decimal SumIncomesForBudget(SqliteConnection conn, int userId, int budgetId, DateTime from, DateTime to)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT IFNULL(SUM(Amount), 0)
FROM Incomes
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) BETWEEN date(@from) AND date(@to);
";
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@bid", budgetId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

            var val = cmd.ExecuteScalar();
            return val == null || val == DBNull.Value ? 0m : Convert.ToDecimal(val, CultureInfo.InvariantCulture);
        }

        public static List<Budget> GetBudgetsForDate(int userId, DateTime date)
        {
            var result = new List<Budget>();

            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, Type, StartDate, EndDate, PlannedAmount
FROM Budgets
WHERE UserId = @uid
  AND IFNULL(IsDeleted,0) = 0
  AND date(StartDate) <= date(@date)
  AND date(EndDate)   >= date(@date)
ORDER BY date(StartDate);
";

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var b = new Budget
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Type = reader.IsDBNull(2) ? "Monthly" : reader.GetString(2),
                    StartDate = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    EndDate = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    PlannedAmount = ReadDecimal(reader, 5)
                };

                if (b.Type.Equals("Inny", StringComparison.OrdinalIgnoreCase))
                    b.Type = "Custom";

                result.Add(b);
            }

            return result;
        }
    }
}
