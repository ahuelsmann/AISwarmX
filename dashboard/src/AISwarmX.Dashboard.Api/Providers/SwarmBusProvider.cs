using System.Globalization;
using Microsoft.Data.Sqlite;
using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Contracts;

namespace AISwarmX.Dashboard.Api.Providers;

/// <summary>
/// Strictly read-only reader for the cross-tool AISwarmX Swarm Bus SQLite database
/// (%LOCALAPPDATA%\MOBAdev\swarm-bus-v2.db with fallback to legacy swarm-bus.db).
/// </summary>
public class SwarmBusProvider
{
    private const int RecentMessageLimit = 20;

    public virtual Task<SwarmBusResponse> GetSwarmBusAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        var dbPath = paths.SwarmBusDbPath;
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            return Task.FromResult(new SwarmBusResponse(
                GeneratedAtUtc: now,
                UnreadCount: 0,
                ActiveLockCount: 0,
                RecentMessages: [],
                ActiveLocks: [],
                Source: new SourceHealth("ok", now, "Swarm bus not initialized yet.")));
        }

        try
        {
            using var connection = SqliteReadOnlyHelper.OpenReadOnly(dbPath);

            var unreadCount = 0;
            using (var unreadCmd = connection.CreateCommand())
            {
                unreadCmd.CommandText = "SELECT COUNT(*) FROM messages WHERE status = 'unread'";
                var raw = unreadCmd.ExecuteScalar();
                if (raw is not null && raw != DBNull.Value)
                {
                    unreadCount = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                }
            }

            var hasMsgWorkspace = TableHasColumn(connection, "messages", "workspace_id");
            var messages = new List<SwarmBusMessageInfo>(RecentMessageLimit);
            using (var msgCmd = connection.CreateCommand())
            {
                var workspaceSelect = hasMsgWorkspace ? ", workspace_id" : string.Empty;
                msgCmd.CommandText = $"""
                    SELECT id, created_at_utc, sender, recipient, topic, body,
                           pr_number, issue_number, priority, reply_to_id, status, acked_by{workspaceSelect}
                    FROM messages
                    ORDER BY id DESC
                    LIMIT @limit
                    """;
                msgCmd.Parameters.AddWithValue("@limit", RecentMessageLimit);

                using var reader = msgCmd.ExecuteReader();
                while (reader.Read())
                {
                    var createdAt = ParseUtc(reader.IsDBNull(1) ? string.Empty : reader.GetString(1), now);
                    messages.Add(new SwarmBusMessageInfo(
                        Id: reader.GetInt64(0),
                        CreatedAtUtc: createdAt,
                        Sender: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        Recipient: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Topic: reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        Body: reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        PrNumber: reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        IssueNumber: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        Priority: reader.IsDBNull(8) ? "normal" : reader.GetString(8),
                        ReplyToId: reader.IsDBNull(9) ? null : reader.GetInt64(9),
                        Status: reader.IsDBNull(10) ? "unread" : reader.GetString(10),
                        AckedBy: reader.IsDBNull(11) ? null : reader.GetString(11),
                        WorkspaceId: hasMsgWorkspace && !reader.IsDBNull(12) ? reader.GetString(12) : null));
                }
            }

            var hasLockWorkspace = TableHasColumn(connection, "locks", "workspace_id");
            var locks = new List<SwarmBusLockInfo>();
            using (var lockCmd = connection.CreateCommand())
            {
                var workspaceSelect = hasLockWorkspace ? ", workspace_id" : string.Empty;
                lockCmd.CommandText = $"""
                    SELECT id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc{workspaceSelect}
                    FROM locks
                    ORDER BY id ASC
                    """;
                using var reader = lockCmd.ExecuteReader();
                while (reader.Read())
                {
                    var expiresAt = ParseUtc(reader.IsDBNull(5) ? string.Empty : reader.GetString(5), DateTimeOffset.MinValue);
                    if (expiresAt <= now)
                    {
                        continue;
                    }

                    var claimedAt = ParseUtc(reader.IsDBNull(4) ? string.Empty : reader.GetString(4), now);
                    locks.Add(new SwarmBusLockInfo(
                        Id: reader.GetInt64(0),
                        Agent: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        PathPattern: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        PrNumber: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        ClaimedAtUtc: claimedAt,
                        ExpiresAtUtc: expiresAt,
                        WorkspaceId: hasLockWorkspace && !reader.IsDBNull(6) ? reader.GetString(6) : null));
                }
            }

            return Task.FromResult(new SwarmBusResponse(
                GeneratedAtUtc: now,
                UnreadCount: unreadCount,
                ActiveLockCount: locks.Count,
                RecentMessages: messages,
                ActiveLocks: locks,
                Source: new SourceHealth("ok", now)));
        }
        catch (SqliteException ex) when (SqliteReadOnlyHelper.IsBusyOrLocked(ex))
        {
            return Task.FromResult(new SwarmBusResponse(
                GeneratedAtUtc: now,
                UnreadCount: 0,
                ActiveLockCount: 0,
                RecentMessages: [],
                ActiveLocks: [],
                Source: new SourceHealth("degraded", now, $"Swarm bus SQLite busy: {ex.Message}")));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new SwarmBusResponse(
                GeneratedAtUtc: now,
                UnreadCount: 0,
                ActiveLockCount: 0,
                RecentMessages: [],
                ActiveLocks: [],
                Source: new SourceHealth("degraded", now, $"Swarm bus read failed: {ex.Message}")));
        }
    }

    private static bool TableHasColumn(SqliteConnection connection, string tableName, string columnName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset ParseUtc(string raw, DateTimeOffset fallback)
    {
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : fallback;
    }
}
