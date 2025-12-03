using Finly.Models;
using Finly.Services;
using Finly.Views.Dialogs;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace Finly.Services
{
    public static class BudgetService
    {

        public static IList<Budget> GetBudgetsForUser(int userId,
                                                      DateTime? from = null,
                                                      DateTime? to = null)
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
            using var conn = DatabaseService.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        INSERT INTO Budgets (UserId, Name, Type, StartDate, EndDate, PlannedAmount)
        VALUES ($uid, $name, $type, $start, $end, $planned);
        SELECT last_insert_rowid();
    ";

            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$name", vm.Name);
            cmd.Parameters.AddWithValue("$type", vm.Type ?? "Budżet");
            cmd.Parameters.AddWithValue("$start", vm.StartDate ?? DateTime.Today);
            cmd.Parameters.AddWithValue("$end", vm.EndDate ?? vm.StartDate ?? DateTime.Today);
            cmd.Parameters.AddWithValue("$planned", vm.PlannedAmount);

            return Convert.ToInt32(cmd.ExecuteScalar());
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
    }
}