using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Finly.Models;
using Finly.Services.Features;

namespace Finly.Services
{
    /// DEMO PSD2: CRUD + „synchronizacja”
    public static class OpenBankingService
    {


        // HELPERY – wszystkie statyczne
        private static int? GetNullableInt32(SqliteDataReader r, int i)
            => r.IsDBNull(i) ? (int?)null : r.GetInt32(i);

        private static string GetStringSafe(SqliteDataReader r, int i)
            => r.IsDBNull(i) ? string.Empty : r.GetString(i);

        private static decimal GetDecimalSafe(SqliteDataReader r, int i)
            => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));

        private static DateTime? GetNullableDate(SqliteDataReader r, int i)
        {
            if (r.IsDBNull(i)) return null;
            var v = r.GetValue(i)?.ToString();
            return DateTime.TryParse(v, out var dt) ? dt : (DateTime?)null;
        }

        public static IEnumerable<BankConnectionModel> GetConnections(int userId)
        {
            using var c = DatabaseService.GetConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT Id, UserId, BankName, AccountHolder, Status, LastSync
                                FROM BankConnections WHERE UserId=@u ORDER BY BankName;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                yield return new BankConnectionModel
                {
                    Id = r.GetInt32(0),
                    UserId = r.GetInt32(1),
                    BankName = r.GetString(2),
                    AccountHolder = r.GetString(3),
                    Status = r.GetString(4),
                    LastSync = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5)
                };
            }
        }

        // TA METODA MUSI BYĆ STATYCZNA
        public static IEnumerable<BankAccountModel> GetAccounts(int userId)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
    a.Id,                           -- 0
    a.ConnectionId,                 -- 1 (NULL-able)
    a.UserId,                       -- 2
    COALESCE(b.BankName,'') AS BankName, -- 3
    a.AccountName,                  -- 4
    a.Iban,                         -- 5
    a.Currency,                     -- 6
    a.Balance,                      -- 7
    a.LastSync                      -- 8 (TEXT/NULL)
FROM BankAccounts a
LEFT JOIN BankConnections b ON b.Id = a.ConnectionId
WHERE a.UserId = @uid
ORDER BY a.AccountName;";
            cmd.Parameters.AddWithValue("@uid", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                yield return new BankAccountModel
                {
                    Id = r.GetInt32(0),
                    ConnectionId = GetNullableInt32(r, 1),
                    UserId = r.GetInt32(2),
                    BankName = GetStringSafe(r, 3),
                    AccountName = GetStringSafe(r, 4),
                    Iban = GetStringSafe(r, 5),
                    Currency = GetStringSafe(r, 6),
                    Balance = GetDecimalSafe(r, 7),
                    LastSync = GetNullableDate(r, 8)
                };
            }
        }


        public static bool ConnectDemo(int userId)
        {
            using var c = DatabaseService.GetConnection();
            using var t = c.BeginTransaction();

            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = @"INSERT INTO BankConnections(UserId, BankName, AccountHolder, Status, LastSync)
                                    VALUES (@u,@n,@h,'Połączono',CURRENT_TIMESTAMP);";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@n", "DEMO Bank");
                cmd.Parameters.AddWithValue("@h", UserService.GetUsername(userId));
                cmd.ExecuteNonQuery();
            }

            long rowId;
            using (var getId = c.CreateCommand())
            {
                getId.Transaction = t;
                getId.CommandText = "SELECT last_insert_rowid();";
                rowId = (long)(getId.ExecuteScalar() ?? 0L);
            }
            var connectionId = (int)rowId;

            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = @"INSERT INTO BankAccounts(UserId, ConnectionId, AccountName, Iban, Currency, Balance, LastSync)
                                    VALUES (@u,@c,'Rachunek osobisty','PL00 0000 0000 0000 0000 0000 0000','PLN', 1523.45, CURRENT_TIMESTAMP),
                                           (@u,@c,'Karta kredytowa','PL11 1111 1111 1111 1111 1111 1111','PLN', -234.50, CURRENT_TIMESTAMP);";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@c", connectionId);
                cmd.ExecuteNonQuery();
            }

            t.Commit();
            return true;
        }

        public static void Disconnect(int connectionId)
        {
            using var c = DatabaseService.GetConnection();
            using var t = c.BeginTransaction();

            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = "DELETE FROM BankAccounts WHERE ConnectionId=@c;";
                cmd.Parameters.AddWithValue("@c", connectionId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = "DELETE FROM BankConnections WHERE Id=@c;";
                cmd.Parameters.AddWithValue("@c", connectionId);
                cmd.ExecuteNonQuery();
            }

            t.Commit();
        }

        public static void SyncNow(int userId)
        {
            using var c = DatabaseService.GetConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"UPDATE BankConnections SET LastSync=CURRENT_TIMESTAMP WHERE UserId=@u;
                                UPDATE BankAccounts    SET LastSync=CURRENT_TIMESTAMP WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }
    }
}

