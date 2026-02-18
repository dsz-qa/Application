using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace Finly.Services.Features
{
    public static class DbDebugTools
    {
        // ✅ NOWE: wygodna wersja bez con
        public static void PrintTablesWithTypeCheck()
        {
            using var con = DatabaseService.GetConnection();
            PrintTablesWithTypeCheck(con);
        }

        // ✅ Twoja wersja istniejąca
        public static void PrintTablesWithTypeCheck(SqliteConnection con)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT name, sql
FROM sqlite_master
WHERE type='table'
  AND sql LIKE '%CHECK%'
  AND sql LIKE '%Type%'
  AND sql LIKE '%IN%';";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0);
                var sql = r.IsDBNull(1) ? "" : r.GetString(1);
                Debug.WriteLine($"TABLE: {name}\nSQL: {sql}\n---------------------");
            }
        }
    }
}
