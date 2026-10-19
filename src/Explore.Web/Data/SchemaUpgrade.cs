using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Web.Data;

// Additive upgrade from the original EnsureCreated schema. Existing rows remain published.
// Later schema changes should use a migration baseline rather than extending this bootstrap.
public static class SchemaUpgrade
{
    public static async Task ApplyAsync(ExploreDb db)
    {
        await db.Database.EnsureCreatedAsync();
        if (db.Database.IsSqlite())
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await connection.OpenAsync();
            try
            {
                var columns = new HashSet<string>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info('Places')";
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
                }
                if (columns.Contains("ImageUrl") && columns.Contains("IsPublished")) return;
                var backupPath = connection.DataSource + ".before-content-management-" +
                    DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".db";
                using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = backupPath }.ToString()))
                {
                    await backup.OpenAsync();
                    connection.BackupDatabase(backup);
                }
                await using var transaction = await db.Database.BeginTransactionAsync();
                if (!columns.Contains("ImageUrl"))
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE Places ADD COLUMN ImageUrl TEXT NOT NULL DEFAULT ''");
                if (!columns.Contains("IsPublished"))
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE Places ADD COLUMN IsPublished INTEGER NOT NULL DEFAULT 1");
                await transaction.CommitAsync();
            }
            finally { await connection.CloseAsync(); }
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Places', 'ImageUrl') IS NULL
                    ALTER TABLE Places ADD ImageUrl nvarchar(2048) NOT NULL DEFAULT '';
                IF COL_LENGTH('Places', 'IsPublished') IS NULL
                    ALTER TABLE Places ADD IsPublished bit NOT NULL DEFAULT 1;
                """);
        }
    }
}
