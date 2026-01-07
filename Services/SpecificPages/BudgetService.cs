using Finly.Models;
using Finly.Services.Features;
using Finly.Views.Dialogs;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace Finly.Services.SpecificPages
{
    public static class BudgetService
    {
        // =========================
        //  MODELE DTO DLA SERWISU
        // =========================

        // To NIE powinno zależeć od Pages (BudgetsPage).
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

        // =========================
        //  CRUD / QUERY
        // =========================

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
                    Type = reader.GetString(3),
                    StartDate = DateTime.Parse(reader.GetString(4)),
                    EndDate = DateTime.Parse(reader.GetString(5)),
                    PlannedAmount = Convert.ToDecimal(reader.GetDouble(6))
                });
            }

            return result;
        }

        public static int InsertBudget(int userId, BudgetDialogViewModel vm)
        {
            using var con = DatabaseService.GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Budgets (UserId, Name, Type, StartDate, EndDate, PlannedAmount)
VALUES (@uid, @name, @type, @start, @end, @planned);
SELECT last_insert_rowid();
";

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@name", vm.Name);
            cmd.Parameters.AddWithValue("@type", vm.Type?.ToString() ?? "Monthly"); // bezpiecznie
            cmd.Parameters.AddWithValue("@start", vm.StartDate ?? DateTime.Today);
            cmd.Parameters.AddWithValue("@end", vm.EndDate ?? vm.StartDate ?? DateTime.Today);
            cmd.Parameters.AddWithValue("@planned", vm.PlannedAmount);

            var id = (long)cmd.ExecuteScalar();
            return (int)id;
        }

        public static void UpdateBudget(Budget b)
        {
            using var conn = DatabaseService.GetConnection();
            conn.Open();

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
            cmd.Parameters.AddWithValue("@name", b.Name);
            cmd.Parameters.AddWithValue("@type", b.Type);
            cmd.Parameters.AddWithValue("@start", b.StartDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@end", b.EndDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@planned", (double)b.PlannedAmount);

            cmd.ExecuteNonQuery();
        }

        public static void DeleteBudget(int id, int userId)
        {
            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Budgets WHERE Id = @id AND UserId = @uid;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.ExecuteNonQuery();
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
            cmd.CommandText = @"
SELECT 
    b.Id,
    b.Name,
    b.Type,
    b.StartDate,
    b.EndDate,
    b.PlannedAmount,
    IFNULL(SUM(DISTINCT e.Amount), 0) AS Spent,
    IFNULL(SUM(DISTINCT i.Amount), 0) AS IncomesForBudget
FROM Budgets b
LEFT JOIN Expenses e 
    ON e.BudgetId = b.Id 
   AND e.UserId = @uid
LEFT JOIN Incomes i
    ON i.BudgetId = b.Id
   AND i.UserId = @uid
WHERE b.UserId = @uid
GROUP BY 
    b.Id, b.Name, b.Type, b.StartDate, b.EndDate, b.PlannedAmount
ORDER BY b.StartDate;
";

            cmd.Parameters.AddWithValue("@uid", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var item = new BudgetSummary
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Type = reader.GetString(2),
                    StartDate = DateTime.Parse(reader.GetString(3)),
                    EndDate = DateTime.Parse(reader.GetString(4)),
                    PlannedAmount = Convert.ToDecimal(reader.GetDouble(5)),
                    Spent = Convert.ToDecimal(reader.GetDouble(6)),
                    IncomesForBudget = Convert.ToDecimal(reader.GetDouble(7))
                };

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
  AND date(EndDate) >= date(@from)
  AND date(StartDate) <= date(@to)
ORDER BY StartDate;
";

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@from", rangeFrom.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", rangeTo.ToString("yyyy-MM-dd"));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt32(0);
                var name = r.GetString(1);
                var start = DateTime.Parse(r.GetString(3));
                var end = DateTime.Parse(r.GetString(4));
                var planned = Convert.ToDecimal(r.GetDouble(5));

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

        private static decimal SumExpensesForBudget(SqliteConnection conn, int userId, int budgetId, DateTime from, DateTime to)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT IFNULL(SUM(Amount), 0)
FROM Expenses
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) >= date(@from)
  AND date(Date) <= date(@to);
";
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@bid", budgetId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

            var val = cmd.ExecuteScalar();
            return val == null || val == DBNull.Value ? 0m : Convert.ToDecimal(val);
        }

        private static decimal SumIncomesForBudget(SqliteConnection conn, int userId, int budgetId, DateTime from, DateTime to)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT IFNULL(SUM(Amount), 0)
FROM Incomes
WHERE UserId = @uid
  AND BudgetId = @bid
  AND date(Date) >= date(@from)
  AND date(Date) <= date(@to);
";
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@bid", budgetId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

            var val = cmd.ExecuteScalar();
            return val == null || val == DBNull.Value ? 0m : Convert.ToDecimal(val);
        }

        public static List<Budget> GetBudgetsForDate(int userId, DateTime date)
        {
            var result = new List<Budget>();

            using var conn = DatabaseService.GetConnection();
            conn.Open(); // <<< TO BYŁO BRAKUJĄCE

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, Type, StartDate, EndDate, PlannedAmount
FROM Budgets
WHERE UserId = @uid
  AND StartDate <= @date
  AND EndDate   >= @date
ORDER BY StartDate;
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
                    Type = reader.GetString(2),
                    StartDate = DateTime.Parse(reader.GetString(3)),
                    EndDate = DateTime.Parse(reader.GetString(4)),
                    PlannedAmount = Convert.ToDecimal(reader.GetDouble(5))
                };

                result.Add(b);
            }

            return result;
        }
    }
}
