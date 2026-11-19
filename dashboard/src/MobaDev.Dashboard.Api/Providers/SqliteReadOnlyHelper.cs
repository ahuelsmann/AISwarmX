using Microsoft.Data.Sqlite;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Shared helper for opening SQLite databases strictly read-only and detecting SQLITE_BUSY locks.
/// </summary>
internal static class SqliteReadOnlyHelper
{
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;

    public static SqliteConnection OpenReadOnly(string dbPath)
    {
        PathWhitelistGuard.ThrowIfForbidden(dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    public static bool IsBusyOrLocked(SqliteException ex)
    {
        return ex.SqliteErrorCode is SqliteBusyErrorCode or SqliteLockedErrorCode;
    }
}
