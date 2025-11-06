using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Finly.Models;

namespace Finly.Services
{
    /// DEMO PSD2: CRUD + „synchronizacja”
    public static class OpenBankingService
    {
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

        public static IEnumerable<BankAccountModel> GetAccounts(int userId)
        {
            using var c = DatabaseService.GetConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT a.Id, a.ConnectionId, a.UserId, b.BankName,
                                       a.AccountName, a.Iban, a.Currency, a.Balance, a.LastSync
                                FROM BankAccounts a
                                LEFT JOIN BankConnections b ON b.Id=a.ConnectionId
                                WHERE a.UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                yield return new BankAccountModel
                {
                    Id = r.GetInt32(0),
                    ConnectionId = r.GetInt32(1),
                    UserId = r.GetInt32(2),
                    BankName = r.IsDBNull(3) ? "" : r.GetString(3),
                    AccountName = r.GetString(4),
                    Iban = r.GetString(5),
                    Currency = r.GetString(6),
                    Balance = r.GetDecimal(7),
                    LastSync = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8)
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

