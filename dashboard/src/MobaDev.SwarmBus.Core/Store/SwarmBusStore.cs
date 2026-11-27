using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MobaDev.SwarmBus.Core.Contracts;
using MobaDev.SwarmBus.Core.Locking;

namespace MobaDev.SwarmBus.Core.Store;

/// <summary>
/// Configuration options for <see cref="SwarmBusStore"/>.
/// </summary>
public sealed class SwarmBusStoreOptions
{
    /// <summary>
    /// Optional time provider for deterministic testing of TTL expirations and wake leases.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Optional wake dispatcher invoked outside the SQLite write transaction when a wake target matches.
    /// When omitted, wake attempts remain in <c>pending</c> state for the 120s lease window until resolved.
    /// </summary>
    public Func<SwarmWakeContext, CancellationToken, Task<SwarmWakeResult>>? WakeDispatcher { get; init; }

    /// <summary>
    /// Maximum retry attempts when SQLite returns <c>SQLITE_BUSY</c> (5) or <c>SQLITE_LOCKED</c> (6).
    /// </summary>
    public int MaxBusyRetries { get; init; } = 24;
}

/// <summary>
/// WAL-mode SQLite persistence store for MOBAi Swarm Bus messages, hierarchical path locks,
/// review claims, work queue items, and workspace metadata.
/// </summary>
public sealed partial class SwarmBusStore
{
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;
    private const string IsoFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private readonly string _dbPath;
    private readonly TimeProvider _timeProvider;
    private readonly Func<SwarmWakeContext, CancellationToken, Task<SwarmWakeResult>>? _wakeDispatcher;
    private readonly int _maxBusyRetries;
    private readonly Lock _initGate = new();
    private volatile bool _initialized;

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadShaRegex();

    public SwarmBusStore(string? dbPath = null, SwarmBusStoreOptions? options = null)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath) ? ResolveDefaultDbPath() : Path.GetFullPath(dbPath.Trim());
        var resolved = options ?? new SwarmBusStoreOptions();
        _timeProvider = resolved.TimeProvider;
        _wakeDispatcher = resolved.WakeDispatcher;
        _maxBusyRetries = Math.Max(1, resolved.MaxBusyRetries);
    }

    public string DatabasePath => _dbPath;

    /// <summary>
    /// Resolves the default Swarm Bus database path (<c>MOBADEV_SWARM_BUS_DB</c> or <c>%LOCALAPPDATA%\MOBAdev\swarm-bus.db</c>).
    /// </summary>
    public static string ResolveDefaultDbPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("MOBADEV_SWARM_BUS_DB");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath.Trim());
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        }

        return Path.Combine(localAppData, "MOBAdev", "swarm-bus.db");
    }

    /// <summary>
    /// Explicitly ensures the SQLite database directory exists, WAL mode is active, and all v3.14.0+ tables/columns are migrated.
    /// </summary>
    public void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initGate)
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            ExecuteWithBusyRetry(() =>
            {
                using var connection = OpenConnectionCore();
                ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");

                RunInImmediateTransactionCore(connection, () =>
                {
                    MigrateAndCreateSchema(connection);
                    return 0;
                });

                return 0;
            });

            _initialized = true;
        }
    }

    public SwarmSendResult SendMessage(SwarmSendRequest request) =>
        SendMessageAsync(request, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<SwarmSendResult> SendMessageAsync(SwarmSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var sender = RequireNonEmpty(request.From, "bus_send requires non-empty \"from\".");
        var recipient = RequireNonEmpty(request.To, "bus_send requires non-empty \"to\".");
        var topic = RequireNonEmpty(request.Topic, "bus_send requires non-empty \"topic\".");
        var body = RequireNonEmpty(request.Body, "bus_send requires non-empty \"body\".");

        var workspaceId = PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);
        var state = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();
        var priority = NormalizePriority(request.Priority);
        var recLower = recipient.ToLowerInvariant();
        var wakeTargetMatches = request.Wake && (recLower.Contains("codex", StringComparison.Ordinal) || recLower == "all");
        var codexThreadId = string.IsNullOrWhiteSpace(request.CodexThreadId) ? null : request.CodexThreadId.Trim();

        var txResult = ExecuteInImmediateTransaction(connection =>
            SendOrDeduplicateInTx(
                connection,
                workspaceId,
                sender,
                recipient,
                topic,
                body,
                request.PrNumber,
                request.IssueNumber,
                request.ReplyToId,
                state,
                priority,
                wakeTargetMatches));

        if (txResult.Mode == SendTxMode.Deduplicated)
        {
            return new SwarmSendResult(
                Ok: true,
                Id: txResult.Id,
                WorkspaceId: workspaceId,
                State: state,
                Deduplicated: true,
                WakeInProgress: txResult.WakeInProgress,
                WakeState: txResult.WakeState);
        }

        if (txResult.Mode == SendTxMode.RetryWake)
        {
            if (_wakeDispatcher is null)
            {
                return new SwarmSendResult(
                    Ok: true,
                    Id: txResult.Id,
                    WorkspaceId: workspaceId,
                    State: state,
                    Deduplicated: true,
                    WakeRetried: true,
                    WakeInProgress: true,
                    WakeState: "pending");
            }

            var wakeRes = await DispatchWakeAsync(
                txResult.Id,
                workspaceId,
                sender,
                recipient,
                request.PrNumber,
                topic,
                txResult.Body ?? body,
                codexThreadId,
                cancellationToken).ConfigureAwait(false);

            return new SwarmSendResult(
                Ok: true,
                Id: txResult.Id,
                WorkspaceId: workspaceId,
                State: state,
                Deduplicated: true,
                WakeRetried: true,
                WakeState: wakeRes.Ok ? "ok" : "failed",
                WakeResults: [wakeRes]);
        }

        List<SwarmWakeResult>? wakeResults = null;
        string? finalWakeState = wakeTargetMatches ? "pending" : null;
        if (wakeTargetMatches)
        {
            if (_wakeDispatcher is not null)
            {
                var wakeRes = await DispatchWakeAsync(
                    txResult.Id,
                    workspaceId,
                    sender,
                    recipient,
                    request.PrNumber,
                    topic,
                    body,
                    codexThreadId,
                    cancellationToken).ConfigureAwait(false);
                wakeResults = [wakeRes];
                finalWakeState = wakeRes.Ok ? "ok" : "failed";
            }
            else
            {
                wakeResults = [];
            }
        }
        else
        {
            wakeResults = [];
        }

        return new SwarmSendResult(
            Ok: true,
            Id: txResult.Id,
            WorkspaceId: workspaceId,
            CreatedAtUtc: txResult.CreatedAt,
            From: sender,
            To: recipient,
            Topic: topic,
            PrNumber: request.PrNumber,
            IssueNumber: request.IssueNumber,
            Priority: priority,
            State: state,
            WakeState: finalWakeState,
            WakeResults: wakeResults);
    }

    /// <summary>
    /// Completes an in-flight wake attempt for <paramref name="messageId"/>, transitioning <c>wake_state</c>
    /// to <c>'ok'</c> or <c>'failed'</c> and clearing <c>wake_attempt_expires_at_utc</c>.
    /// </summary>
    public void CompleteWakeAttempt(long messageId, bool ok)
    {
        ExecuteInImmediateTransaction(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE messages
                SET wake_state = @wakeState,
                    wake_ok = @wakeOk,
                    wake_attempt_expires_at_utc = NULL
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@wakeState", ok ? "ok" : "failed");
            cmd.Parameters.AddWithValue("@wakeOk", ok ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", messageId);
            cmd.ExecuteNonQuery();
            return 0;
        });
    }

    public SwarmInboxResult GetInbox(
        string agent = "all",
        bool unreadOnly = true,
        int limit = 20,
        string? workspaceId = null)
    {
        var normalizedAgent = string.IsNullOrWhiteSpace(agent) ? "all" : agent.Trim();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        var allWorkspaces = string.Equals(workspaceId?.Trim(), "*", StringComparison.Ordinal);
        var normalizedWorkspace = allWorkspaces ? "*" : PathPatternMatcher.NormalizeWorkspaceId(workspaceId);
        var includeAllRecipients = normalizedAgent.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                   normalizedAgent == "*";

        var messages = ExecuteRead(connection =>
        {
            using var cmd = connection.CreateCommand();
            var clauses = new List<string>();
            if (!allWorkspaces)
            {
                clauses.Add("workspace_id = @workspaceId");
                cmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
            }

            if (!includeAllRecipients)
            {
                clauses.Add("(lower(recipient) = lower(@agent) OR lower(recipient) = 'all')");
                cmd.Parameters.AddWithValue("@agent", normalizedAgent);
            }

            if (unreadOnly)
            {
                clauses.Add("status = 'unread'");
            }

            var whereSql = clauses.Count > 0 ? $"WHERE {string.Join(" AND ", clauses)}" : string.Empty;
            cmd.CommandText = $"""
                SELECT id, workspace_id, created_at_utc, sender, recipient, topic, body,
                       pr_number, issue_number, priority, reply_to_id, status,
                       acked_by, acked_at_utc, state, wake_ok, wake_state, wake_attempt_expires_at_utc
                FROM messages
                {whereSql}
                ORDER BY id DESC
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@limit", clampedLimit);

            return ReadMessages(cmd);
        });

        return new SwarmInboxResult(
            Agent: normalizedAgent,
            WorkspaceId: normalizedWorkspace,
            UnreadOnly: unreadOnly,
            Count: messages.Count,
            Messages: messages);
    }

    public Task<SwarmInboxResult> GetInboxAsync(
        string agent = "all",
        bool unreadOnly = true,
        int limit = 20,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetInbox(agent, unreadOnly, limit, workspaceId));
    }

    public SwarmAckResult AckMessage(long messageId, string agent, string? reply = null) =>
        AckMessageAsync(messageId, agent, reply, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<SwarmAckResult> AckMessageAsync(
        long messageId,
        string agent,
        string? reply = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (messageId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(messageId), "bus_ack requires positive \"message_id\".");
        }

        var normalizedAgent = RequireNonEmpty(agent, "bus_ack requires non-empty \"agent\".");
        var trimmedReply = string.IsNullOrWhiteSpace(reply) ? null : reply.Trim();

        var ackInfo = ExecuteInImmediateTransaction(connection =>
        {
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = """
                SELECT id, workspace_id, sender, topic, pr_number, issue_number
                FROM messages
                WHERE id = @id
                """;
            selectCmd.Parameters.AddWithValue("@id", messageId);

            using var reader = selectCmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException($"Message #{messageId} not found.");
            }

            var workspaceId = reader.IsDBNull(1) ? SwarmBusConstants.DefaultWorkspaceId : reader.GetString(1);
            var origSender = reader.GetString(2);
            var origTopic = reader.GetString(3);
            int? prNumber = reader.IsDBNull(4) ? null : reader.GetInt32(4);
            int? issueNumber = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            reader.Close();

            var ackedAt = _timeProvider.GetUtcNow();
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = """
                UPDATE messages
                SET status = 'acked', acked_by = @agent, acked_at_utc = @ackedAt
                WHERE id = @id
                """;
            updateCmd.Parameters.AddWithValue("@agent", normalizedAgent);
            updateCmd.Parameters.AddWithValue("@ackedAt", FormatUtc(ackedAt));
            updateCmd.Parameters.AddWithValue("@id", messageId);
            updateCmd.ExecuteNonQuery();

            return (workspaceId, origSender, origTopic, prNumber, issueNumber, ackedAt);
        });

        SwarmSendResult? replyResult = null;
        if (trimmedReply is not null)
        {
            replyResult = await SendMessageAsync(new SwarmSendRequest(
                From: normalizedAgent,
                To: ackInfo.origSender,
                Topic: $"re:{ackInfo.origTopic}",
                Body: trimmedReply,
                PrNumber: ackInfo.prNumber,
                IssueNumber: ackInfo.issueNumber,
                ReplyToId: messageId,
                WorkspaceId: ackInfo.workspaceId), cancellationToken).ConfigureAwait(false);
        }

        return new SwarmAckResult(
            Ok: true,
            AckedMessageId: messageId,
            AckedBy: normalizedAgent,
            AckedAtUtc: ackInfo.ackedAt,
            ReplyMessage: replyResult);
    }

    public LockClaimResult ClaimLocks(LockClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agent = RequireNonEmpty(request.Agent, "bus_locks claim requires non-empty \"agent\".");
        var workspaceId = PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);
        var cleanPaths = (request.Paths ?? [])
            .Select(PathPatternMatcher.CanonicalizeForStorage)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleanPaths.Count == 0)
        {
            throw new ArgumentException("bus_locks claim requires non-empty \"paths\".", nameof(request));
        }

        var ttlMinutes = Math.Clamp(request.TtlMinutes, SwarmBusConstants.MinLockTtlMinutes, SwarmBusConstants.MaxLockTtlMinutes);

        return ExecuteInImmediateTransaction(connection =>
        {
            var now = _timeProvider.GetUtcNow();
            var nowIso = FormatUtc(now);
            var expiresAt = now.AddMinutes(ttlMinutes);
            var expiresIso = FormatUtc(expiresAt);

            PruneExpiredLocksInTx(connection, now);

            var existingLocks = ReadLocksInWorkspace(connection, workspaceId);
            var conflicts = new List<LockConflict>();
            foreach (var reqPath in cleanPaths)
            {
                foreach (var ex in existingLocks)
                {
                    if (!ex.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase) &&
                        PathPatternMatcher.LocksOverlap(workspaceId, reqPath, ex.WorkspaceId, ex.PathPattern))
                    {
                        conflicts.Add(new LockConflict(
                            RequestedPath: reqPath,
                            ConflictingAgent: ex.Agent,
                            ConflictingPath: ex.PathPattern,
                            ConflictingPr: ex.PrNumber,
                            ExpiresAtUtc: ex.ExpiresAtUtc,
                            WorkspaceId: workspaceId));
                    }
                }
            }

            if (conflicts.Count > 0)
            {
                return new LockClaimResult(
                    Ok: false,
                    WorkspaceId: workspaceId,
                    ClaimedPaths: [],
                    Conflicts: conflicts,
                    Locks: existingLocks);
            }

            foreach (var reqPath in cleanPaths)
            {
                var normalizedReq = PathPatternMatcher.NormalizePath(reqPath);
                var existingSameNormalized = existingLocks
                    .Where(l => string.Equals(PathPatternMatcher.NormalizePath(l.PathPattern), normalizedReq, StringComparison.Ordinal) &&
                                !string.Equals(l.PathPattern, reqPath, StringComparison.Ordinal))
                    .ToList();
                foreach (var staleCase in existingSameNormalized)
                {
                    using var delCmd = connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM locks WHERE id = @id";
                    delCmd.Parameters.AddWithValue("@id", staleCase.Id);
                    delCmd.ExecuteNonQuery();
                }

                using var upsert = connection.CreateCommand();
                upsert.CommandText = """
                    INSERT INTO locks (workspace_id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
                    VALUES (@workspaceId, @agent, @pathPattern, @prNumber, @claimedAt, @expiresAt)
                    ON CONFLICT(workspace_id, path_pattern) DO UPDATE SET
                        agent = excluded.agent,
                        pr_number = excluded.pr_number,
                        claimed_at_utc = excluded.claimed_at_utc,
                        expires_at_utc = excluded.expires_at_utc
                    """;
                upsert.Parameters.AddWithValue("@workspaceId", workspaceId);
                upsert.Parameters.AddWithValue("@agent", agent);
                upsert.Parameters.AddWithValue("@pathPattern", reqPath);
                upsert.Parameters.AddWithValue("@prNumber", (object?)request.PrNumber ?? DBNull.Value);
                upsert.Parameters.AddWithValue("@claimedAt", nowIso);
                upsert.Parameters.AddWithValue("@expiresAt", expiresIso);
                upsert.ExecuteNonQuery();
            }

            var active = ReadLocksInWorkspace(connection, workspaceId);
            return new LockClaimResult(
                Ok: true,
                WorkspaceId: workspaceId,
                ClaimedPaths: cleanPaths,
                Conflicts: [],
                Locks: active);
        });
    }

    public Task<LockClaimResult> ClaimLocksAsync(LockClaimRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClaimLocks(request));
    }

    public LockReleaseResult ReleaseLocks(LockReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agent = string.IsNullOrWhiteSpace(request.Agent) ? null : request.Agent.Trim();
        var cleanPaths = (request.Paths ?? [])
            .Select(PathPatternMatcher.NormalizePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.Ordinal);
        var workspaceId = PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);

        if (agent is null && cleanPaths.Count == 0 && !request.PrNumber.HasValue)
        {
            throw new ArgumentException("bus_locks release requires \"agent\", \"paths\", or \"prNumber\".", nameof(request));
        }

        return ExecuteInImmediateTransaction(connection =>
        {
            PruneExpiredLocksInTx(connection, _timeProvider.GetUtcNow());

            var candidates = ReadLocksInWorkspace(connection, workspaceId);
            var idsToDelete = new List<long>();
            foreach (var candidate in candidates)
            {
                if (agent is not null && !candidate.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (request.PrNumber.HasValue && candidate.PrNumber != request.PrNumber.Value)
                {
                    continue;
                }

                if (cleanPaths.Count > 0 && !cleanPaths.Contains(PathPatternMatcher.NormalizePath(candidate.PathPattern)))
                {
                    continue;
                }

                idsToDelete.Add(candidate.Id);
            }

            foreach (var id in idsToDelete)
            {
                using var del = connection.CreateCommand();
                del.CommandText = "DELETE FROM locks WHERE id = @id";
                del.Parameters.AddWithValue("@id", id);
                del.ExecuteNonQuery();
            }

            var remaining = ReadLocksInWorkspace(connection, workspaceId);
            return new LockReleaseResult(
                Ok: true,
                WorkspaceId: workspaceId,
                RemovedCount: idsToDelete.Count,
                RemainingLocks: remaining);
        });
    }

    public Task<LockReleaseResult> ReleaseLocksAsync(LockReleaseRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReleaseLocks(request));
    }

    public IReadOnlyList<PathLockLease> ListLocks(string? workspaceId = null)
    {
        var allWorkspaces = string.Equals(workspaceId?.Trim(), "*", StringComparison.Ordinal);
        var normalizedWorkspace = allWorkspaces ? "*" : PathPatternMatcher.NormalizeWorkspaceId(workspaceId);

        return ExecuteInImmediateTransaction(connection =>
        {
            PruneExpiredLocksInTx(connection, _timeProvider.GetUtcNow());
            return allWorkspaces
                ? ReadAllLocks(connection)
                : ReadLocksInWorkspace(connection, normalizedWorkspace);
        });
    }

    public Task<IReadOnlyList<PathLockLease>> ListLocksAsync(string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ListLocks(workspaceId));
    }

    public ReviewClaimResult ReviewClaim(ReviewClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var action = string.IsNullOrWhiteSpace(request.Action) ? "claim" : request.Action.Trim().ToLowerInvariant();
        var workspaceId = PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);

        return action switch
        {
            "claim" => ClaimReviewSlot(request, workspaceId),
            "complete" => CompleteReviewSlot(request, workspaceId),
            "release" => ReleaseReviewSlot(request, workspaceId),
            "list" => ListReviewSlots(request),
            _ => throw new ArgumentException($"Unsupported bus_review_claim action '{action}'. Use: claim, complete, release, list.", nameof(request))
        };
    }

    public Task<ReviewClaimResult> ReviewClaimAsync(ReviewClaimRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReviewClaim(request));
    }

    public WorkQueueMutationResult QueueUpsert(WorkQueueUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IssueNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "bus_queue upsert requires positive integer \"issue_number\".");
        }

        var workspaceId = PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);
        var incomingStatus = string.IsNullOrWhiteSpace(request.Status) ? "queued" : request.Status.Trim().ToLowerInvariant();
        if (!SwarmBusConstants.ValidQueueStatuses.Contains(incomingStatus))
        {
            throw new ArgumentException($"Invalid work_queue status '{incomingStatus}'. Expected: queued, claimed, done, removed.", nameof(request));
        }

        var rawTitle = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        var priority = request.Priority ?? 2;
        var riskClass = string.IsNullOrWhiteSpace(request.RiskClass) ? null : request.RiskClass.Trim().ToUpperInvariant();

        return ExecuteInImmediateTransaction(connection =>
        {
            var nowIso = FormatUtc(_timeProvider.GetUtcNow());

            if (rawTitle is null && incomingStatus is "done" or "removed")
            {
                var existing = ReadWorkQueueItem(connection, workspaceId, request.IssueNumber);
                if (existing is not null)
                {
                    using var updateCmd = connection.CreateCommand();
                    updateCmd.CommandText = """
                        UPDATE work_queue
                        SET status = @status,
                            claimed_by = NULL,
                            claimed_at_utc = NULL,
                            lease_expires_utc = NULL,
                            updated_at_utc = @updatedAt
                        WHERE workspace_id = @workspaceId AND issue_number = @issueNumber
                        """;
                    updateCmd.Parameters.AddWithValue("@status", incomingStatus);
                    updateCmd.Parameters.AddWithValue("@updatedAt", nowIso);
                    updateCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
                    updateCmd.Parameters.AddWithValue("@issueNumber", request.IssueNumber);
                    updateCmd.ExecuteNonQuery();

                    return new WorkQueueMutationResult(
                        Ok: true,
                        Action: "upsert",
                        WorkspaceId: workspaceId,
                        IssueNumber: request.IssueNumber,
                        Changed: true,
                        Item: ReadWorkQueueItem(connection, workspaceId, request.IssueNumber));
                }
            }

            if (rawTitle is null)
            {
                throw new ArgumentException("bus_queue upsert requires non-empty \"title\".", nameof(request));
            }

            using var upsertCmd = connection.CreateCommand();
            upsertCmd.CommandText = """
                INSERT INTO work_queue (workspace_id, issue_number, title, priority, risk_class, status, updated_at_utc)
                VALUES (@workspaceId, @issueNumber, @title, @priority, @riskClass, @status, @updatedAt)
                ON CONFLICT(workspace_id, issue_number) DO UPDATE SET
                    title = excluded.title,
                    priority = excluded.priority,
                    risk_class = excluded.risk_class,
                    status = CASE
                        WHEN excluded.status IN ('done', 'removed') THEN excluded.status
                        WHEN work_queue.status = 'claimed' THEN 'claimed'
                        ELSE excluded.status
                    END,
                    claimed_by = CASE WHEN excluded.status IN ('done', 'removed') THEN NULL ELSE work_queue.claimed_by END,
                    claimed_at_utc = CASE WHEN excluded.status IN ('done', 'removed') THEN NULL ELSE work_queue.claimed_at_utc END,
                    lease_expires_utc = CASE WHEN excluded.status IN ('done', 'removed') THEN NULL ELSE work_queue.lease_expires_utc END,
                    updated_at_utc = excluded.updated_at_utc
                """;
            upsertCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
            upsertCmd.Parameters.AddWithValue("@issueNumber", request.IssueNumber);
            upsertCmd.Parameters.AddWithValue("@title", rawTitle);
            upsertCmd.Parameters.AddWithValue("@priority", priority);
            upsertCmd.Parameters.AddWithValue("@riskClass", (object?)riskClass ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@status", incomingStatus);
            upsertCmd.Parameters.AddWithValue("@updatedAt", nowIso);
            upsertCmd.ExecuteNonQuery();

            return new WorkQueueMutationResult(
                Ok: true,
                Action: "upsert",
                WorkspaceId: workspaceId,
                IssueNumber: request.IssueNumber,
                Changed: true,
                Item: ReadWorkQueueItem(connection, workspaceId, request.IssueNumber));
        });
    }

    public Task<WorkQueueMutationResult> QueueUpsertAsync(WorkQueueUpsertRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueueUpsert(request));
    }

    public WorkQueueNextResult QueueNext(string agent, string? workspaceId = null)
    {
        var normalizedAgent = RequireNonEmpty(agent, "bus_queue next requires non-empty \"agent\".");
        var normalizedWorkspace = PathPatternMatcher.NormalizeWorkspaceId(workspaceId);

        return ExecuteInImmediateTransaction(connection =>
        {
            var now = _timeProvider.GetUtcNow();
            var nowIso = FormatUtc(now);
            var leaseExpiresIso = FormatUtc(now.AddHours(SwarmBusConstants.DefaultQueueLeaseHours));

            // 1. Resume own active claim first and renew +4h lease.
            var ownItems = ReadWorkQueueItems(connection, normalizedWorkspace, "claimed")
                .Where(item => string.Equals(item.ClaimedBy, normalizedAgent, StringComparison.OrdinalIgnoreCase) &&
                               item.LeaseExpiresUtc.HasValue &&
                               item.LeaseExpiresUtc.Value > now)
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.Id)
                .ToList();

            if (ownItems.Count > 0)
            {
                var ownActive = ownItems[0];
                using var renewCmd = connection.CreateCommand();
                renewCmd.CommandText = """
                    UPDATE work_queue
                    SET lease_expires_utc = @leaseExpires, updated_at_utc = @updatedAt
                    WHERE id = @id
                    """;
                renewCmd.Parameters.AddWithValue("@leaseExpires", leaseExpiresIso);
                renewCmd.Parameters.AddWithValue("@updatedAt", nowIso);
                renewCmd.Parameters.AddWithValue("@id", ownActive.Id);
                renewCmd.ExecuteNonQuery();

                var refreshed = ReadWorkQueueItemById(connection, ownActive.Id);
                return new WorkQueueNextResult(
                    Ok: true,
                    WorkspaceId: normalizedWorkspace,
                    Resumed: true,
                    CodeWip: 0,
                    ActiveClaimedCodeCount: 0,
                    EffectiveCodeWip: 0,
                    Item: refreshed);
            }

            // 2. Compute effectiveCodeWip = baseCodeWip + active claimed non-L items (unclassified counts as code).
            var baseCodeWip = ReadMetaInt(connection, normalizedWorkspace, "code_wip");
            var allClaimed = ReadWorkQueueItems(connection, normalizedWorkspace, "claimed");
            var activeClaimedCodeCount = allClaimed.Count(item =>
                (!item.LeaseExpiresUtc.HasValue || item.LeaseExpiresUtc.Value > now) &&
                !string.Equals(item.RiskClass?.Trim(), "L", StringComparison.OrdinalIgnoreCase));

            var effectiveCodeWip = baseCodeWip + activeClaimedCodeCount;
            var restrictToLowRisk = effectiveCodeWip >= SwarmBusConstants.CodeWipClaimLimit;

            // 3. Pick highest-priority, FIFO eligible item (queued or expired lease).
            var allItems = ReadWorkQueueItems(connection, normalizedWorkspace, statusFilter: null);
            var candidate = allItems
                .Where(item =>
                    (string.Equals(item.Status, "queued", StringComparison.Ordinal) ||
                     (string.Equals(item.Status, "claimed", StringComparison.Ordinal) &&
                      item.LeaseExpiresUtc.HasValue &&
                      item.LeaseExpiresUtc.Value <= now)) &&
                    (!restrictToLowRisk || string.Equals(item.RiskClass?.Trim(), "L", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.Id)
                .FirstOrDefault();

            if (candidate is null)
            {
                return new WorkQueueNextResult(
                    Ok: true,
                    WorkspaceId: normalizedWorkspace,
                    Resumed: false,
                    CodeWip: baseCodeWip,
                    ActiveClaimedCodeCount: activeClaimedCodeCount,
                    EffectiveCodeWip: effectiveCodeWip,
                    Item: null,
                    Reason: restrictToLowRisk
                        ? $"effective_code_wip ({effectiveCodeWip}) >= {SwarmBusConstants.CodeWipClaimLimit} and no eligible L-class work item is queued."
                        : "No queued or expired-lease work items available.");
            }

            using var claimCmd = connection.CreateCommand();
            claimCmd.CommandText = """
                UPDATE work_queue
                SET status = 'claimed',
                    claimed_by = @agent,
                    claimed_at_utc = @claimedAt,
                    lease_expires_utc = @leaseExpires,
                    updated_at_utc = @updatedAt
                WHERE id = @id
                """;
            claimCmd.Parameters.AddWithValue("@agent", normalizedAgent);
            claimCmd.Parameters.AddWithValue("@claimedAt", nowIso);
            claimCmd.Parameters.AddWithValue("@leaseExpires", leaseExpiresIso);
            claimCmd.Parameters.AddWithValue("@updatedAt", nowIso);
            claimCmd.Parameters.AddWithValue("@id", candidate.Id);
            claimCmd.ExecuteNonQuery();

            var claimed = ReadWorkQueueItemById(connection, candidate.Id);
            return new WorkQueueNextResult(
                Ok: true,
                WorkspaceId: normalizedWorkspace,
                Resumed: false,
                CodeWip: baseCodeWip,
                ActiveClaimedCodeCount: activeClaimedCodeCount,
                EffectiveCodeWip: effectiveCodeWip,
                Item: claimed);
        });
    }

    public Task<WorkQueueNextResult> QueueNextAsync(string agent, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueueNext(agent, workspaceId));
    }

    public WorkQueueMutationResult QueueRelease(int issueNumber, string? agent = null, string? workspaceId = null)
    {
        if (issueNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(issueNumber), "bus_queue release requires positive integer \"issue_number\".");
        }

        var normalizedWorkspace = PathPatternMatcher.NormalizeWorkspaceId(workspaceId);
        var normalizedAgent = string.IsNullOrWhiteSpace(agent) ? null : agent.Trim();

        return ExecuteInImmediateTransaction(connection =>
        {
            var nowIso = FormatUtc(_timeProvider.GetUtcNow());
            using var cmd = connection.CreateCommand();
            cmd.CommandText = normalizedAgent is not null
                ? """
                  UPDATE work_queue
                  SET status = 'queued', claimed_by = NULL, claimed_at_utc = NULL, lease_expires_utc = NULL, updated_at_utc = @updatedAt
                  WHERE workspace_id = @workspaceId AND issue_number = @issueNumber AND status = 'claimed' AND lower(claimed_by) = lower(@agent)
                  """
                : """
                  UPDATE work_queue
                  SET status = 'queued', claimed_by = NULL, claimed_at_utc = NULL, lease_expires_utc = NULL, updated_at_utc = @updatedAt
                  WHERE workspace_id = @workspaceId AND issue_number = @issueNumber AND status = 'claimed'
                  """;
            cmd.Parameters.AddWithValue("@updatedAt", nowIso);
            cmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
            cmd.Parameters.AddWithValue("@issueNumber", issueNumber);
            if (normalizedAgent is not null)
            {
                cmd.Parameters.AddWithValue("@agent", normalizedAgent);
            }

            var changed = cmd.ExecuteNonQuery() > 0;
            return new WorkQueueMutationResult(
                Ok: true,
                Action: "release",
                WorkspaceId: normalizedWorkspace,
                IssueNumber: issueNumber,
                Changed: changed,
                Item: ReadWorkQueueItem(connection, normalizedWorkspace, issueNumber));
        });
    }

    public Task<WorkQueueMutationResult> QueueReleaseAsync(
        int issueNumber,
        string? agent = null,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueueRelease(issueNumber, agent, workspaceId));
    }

    public WorkQueueMutationResult QueueDone(int issueNumber, string? workspaceId = null)
    {
        if (issueNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(issueNumber), "bus_queue done requires positive integer \"issue_number\".");
        }

        var normalizedWorkspace = PathPatternMatcher.NormalizeWorkspaceId(workspaceId);

        return ExecuteInImmediateTransaction(connection =>
        {
            var nowIso = FormatUtc(_timeProvider.GetUtcNow());
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE work_queue
                SET status = 'done', lease_expires_utc = NULL, updated_at_utc = @updatedAt
                WHERE workspace_id = @workspaceId AND issue_number = @issueNumber
                """;
            cmd.Parameters.AddWithValue("@updatedAt", nowIso);
            cmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
            cmd.Parameters.AddWithValue("@issueNumber", issueNumber);

            var changed = cmd.ExecuteNonQuery() > 0;
            return new WorkQueueMutationResult(
                Ok: true,
                Action: "done",
                WorkspaceId: normalizedWorkspace,
                IssueNumber: issueNumber,
                Changed: changed,
                Item: ReadWorkQueueItem(connection, normalizedWorkspace, issueNumber));
        });
    }

    public Task<WorkQueueMutationResult> QueueDoneAsync(
        int issueNumber,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueueDone(issueNumber, workspaceId));
    }

    public WorkQueueListResult QueueList(string? status = null, string? workspaceId = null)
    {
        var normalizedWorkspace = PathPatternMatcher.NormalizeWorkspaceId(workspaceId);
        var statusFilter = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        if (statusFilter is not null && !SwarmBusConstants.ValidQueueStatuses.Contains(statusFilter))
        {
            throw new ArgumentException($"Invalid status '{statusFilter}'. Expected: queued, claimed, done, removed.", nameof(status));
        }

        return ExecuteRead(connection =>
        {
            var items = ReadWorkQueueItems(connection, normalizedWorkspace, statusFilter);
            var meta = ReadMetaMap(connection, normalizedWorkspace);
            var codeWip = meta.TryGetValue("code_wip", out var rawWip) && int.TryParse(rawWip, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            return new WorkQueueListResult(
                Ok: true,
                WorkspaceId: normalizedWorkspace,
                CodeWip: codeWip,
                Meta: meta,
                Items: items);
        });
    }

    public Task<WorkQueueListResult> QueueListAsync(
        string? status = null,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueueList(status, workspaceId));
    }

    public void SyncMeta(string key, string value, string? workspaceId = null)
    {
        var normalizedKey = RequireNonEmpty(key, "bus_queue sync-meta requires non-empty \"meta_key\".");
        ArgumentNullException.ThrowIfNull(value);
        var normalizedWorkspace = PathPatternMatcher.NormalizeWorkspaceId(workspaceId);

        ExecuteInImmediateTransaction(connection =>
        {
            var nowIso = FormatUtc(_timeProvider.GetUtcNow());
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO meta (workspace_id, key, value, updated_at_utc)
                VALUES (@workspaceId, @key, @value, @updatedAt)
                ON CONFLICT(workspace_id, key) DO UPDATE SET
                    value = excluded.value,
                    updated_at_utc = excluded.updated_at_utc
                """;
            cmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
            cmd.Parameters.AddWithValue("@key", normalizedKey);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@updatedAt", nowIso);
            cmd.ExecuteNonQuery();
            return 0;
        });
    }

    public Task SyncMetaAsync(string key, string value, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SyncMeta(key, value, workspaceId);
        return Task.CompletedTask;
    }

    public SwarmBusStatusSnapshot GetStatus(int limit = 15, string? workspaceId = null)
    {
        var clampedLimit = Math.Clamp(limit, 1, 50);
        var allWorkspaces = string.Equals(workspaceId?.Trim(), "*", StringComparison.Ordinal);
        var normalizedWorkspace = allWorkspaces ? "*" : PathPatternMatcher.NormalizeWorkspaceId(workspaceId);

        return ExecuteInImmediateTransaction(connection =>
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpiredLocksInTx(connection, now);

            var unreadByRecipient = new List<RecipientUnreadCount>();
            using (var unreadByCmd = connection.CreateCommand())
            {
                unreadByCmd.CommandText = allWorkspaces
                    ? """
                      SELECT recipient, COUNT(*) AS unread_count
                      FROM messages
                      WHERE status = 'unread'
                      GROUP BY recipient
                      ORDER BY unread_count DESC, recipient ASC
                      """
                    : """
                      SELECT recipient, COUNT(*) AS unread_count
                      FROM messages
                      WHERE workspace_id = @workspaceId AND status = 'unread'
                      GROUP BY recipient
                      ORDER BY unread_count DESC, recipient ASC
                      """;
                if (!allWorkspaces)
                {
                    unreadByCmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
                }

                using var reader = unreadByCmd.ExecuteReader();
                while (reader.Read())
                {
                    unreadByRecipient.Add(new RecipientUnreadCount(
                        Recipient: reader.GetString(0),
                        UnreadCount: reader.GetInt32(1)));
                }
            }

            var totalUnread = 0;
            using (var totalCmd = connection.CreateCommand())
            {
                totalCmd.CommandText = allWorkspaces
                    ? "SELECT COUNT(*) FROM messages WHERE status = 'unread'"
                    : "SELECT COUNT(*) FROM messages WHERE workspace_id = @workspaceId AND status = 'unread'";
                if (!allWorkspaces)
                {
                    totalCmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
                }

                var scalar = totalCmd.ExecuteScalar();
                if (scalar is not null && scalar != DBNull.Value)
                {
                    totalUnread = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
                }
            }

            var activeLocks = allWorkspaces
                ? ReadAllLocks(connection)
                : ReadLocksInWorkspace(connection, normalizedWorkspace);

            List<SwarmMessageEnvelope> recentMessages;
            using (var recentCmd = connection.CreateCommand())
            {
                recentCmd.CommandText = allWorkspaces
                    ? """
                      SELECT id, workspace_id, created_at_utc, sender, recipient, topic, body,
                             pr_number, issue_number, priority, reply_to_id, status,
                             acked_by, acked_at_utc, state, wake_ok, wake_state, wake_attempt_expires_at_utc
                      FROM messages
                      ORDER BY id DESC
                      LIMIT @limit
                      """
                    : """
                      SELECT id, workspace_id, created_at_utc, sender, recipient, topic, body,
                             pr_number, issue_number, priority, reply_to_id, status,
                             acked_by, acked_at_utc, state, wake_ok, wake_state, wake_attempt_expires_at_utc
                      FROM messages
                      WHERE workspace_id = @workspaceId
                      ORDER BY id DESC
                      LIMIT @limit
                      """;
                if (!allWorkspaces)
                {
                    recentCmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
                }

                recentCmd.Parameters.AddWithValue("@limit", clampedLimit);
                recentMessages = ReadMessages(recentCmd);
            }

            return new SwarmBusStatusSnapshot(
                GeneratedAtUtc: now,
                WorkspaceId: normalizedWorkspace,
                TotalUnread: totalUnread,
                UnreadByRecipient: unreadByRecipient,
                ActiveLocks: activeLocks,
                RecentMessages: recentMessages);
        });
    }

    public Task<SwarmBusStatusSnapshot> GetStatusAsync(int limit = 15, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetStatus(limit, workspaceId));
    }

    private ReviewClaimResult ClaimReviewSlot(ReviewClaimRequest request, string workspaceId)
    {
        var agent = RequireNonEmpty(request.Agent, "bus_review_claim claim requires non-empty \"agent\".");
        var (prNumber, headSha, role) = ValidateReviewClaimTarget(request);
        var rawTtl = request.TtlMinutes ?? SwarmBusConstants.DefaultReviewClaimTtlMinutes;
        var ttlMinutes = Math.Clamp(rawTtl, SwarmBusConstants.MinReviewClaimTtlMinutes, SwarmBusConstants.MaxReviewClaimTtlMinutes);

        return ExecuteInImmediateTransaction(connection =>
        {
            var now = _timeProvider.GetUtcNow();
            var claimedAtIso = FormatUtc(now);
            var expiresAtIso = FormatUtc(now.AddMinutes(ttlMinutes));

            var existing = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);
            if (existing is null)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = """
                    INSERT INTO review_claims (workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc)
                    VALUES (@workspaceId, @prNumber, @headSha, @role, @agent, @claimedAt, 'claimed', @expiresAt, NULL)
                    """;
                insertCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
                insertCmd.Parameters.AddWithValue("@prNumber", prNumber);
                insertCmd.Parameters.AddWithValue("@headSha", headSha);
                insertCmd.Parameters.AddWithValue("@role", role);
                insertCmd.Parameters.AddWithValue("@agent", agent);
                insertCmd.Parameters.AddWithValue("@claimedAt", claimedAtIso);
                insertCmd.Parameters.AddWithValue("@expiresAt", expiresAtIso);
                insertCmd.ExecuteNonQuery();

                var inserted = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);
                return new ReviewClaimResult(
                    Ok: true,
                    Action: "claim",
                    WorkspaceId: workspaceId,
                    Claimed: true,
                    Reclaimed: false,
                    PrNumber: prNumber,
                    HeadSha: headSha,
                    Role: role,
                    Status: "claimed",
                    Claim: inserted);
            }

            if (string.Equals(existing.Status, "completed", StringComparison.Ordinal))
            {
                return new ReviewClaimResult(
                    Ok: true,
                    Action: "claim",
                    WorkspaceId: workspaceId,
                    Claimed: false,
                    Reason: "already_completed",
                    Holder: existing.Agent,
                    PrNumber: existing.PrNumber,
                    HeadSha: existing.HeadSha,
                    Role: existing.Role,
                    Status: "completed",
                    Claim: existing);
            }

            var isActiveClaim = string.Equals(existing.Status, "claimed", StringComparison.Ordinal) &&
                                existing.ExpiresAtUtc.HasValue &&
                                existing.ExpiresAtUtc.Value > now;
            if (isActiveClaim)
            {
                return new ReviewClaimResult(
                    Ok: true,
                    Action: "claim",
                    WorkspaceId: workspaceId,
                    Claimed: false,
                    Reason: "already_claimed",
                    AlreadyClaimedBySelf: existing.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase),
                    Holder: existing.Agent,
                    PrNumber: existing.PrNumber,
                    HeadSha: existing.HeadSha,
                    Role: existing.Role,
                    Status: "claimed",
                    Claim: existing);
            }

            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = """
                UPDATE review_claims
                SET head_sha = @headSha,
                    agent = @agent,
                    status = 'claimed',
                    claimed_at_utc = @claimedAt,
                    expires_at_utc = @expiresAt,
                    completed_at_utc = NULL
                WHERE id = @id
                """;
            updateCmd.Parameters.AddWithValue("@headSha", headSha);
            updateCmd.Parameters.AddWithValue("@agent", agent);
            updateCmd.Parameters.AddWithValue("@claimedAt", claimedAtIso);
            updateCmd.Parameters.AddWithValue("@expiresAt", expiresAtIso);
            updateCmd.Parameters.AddWithValue("@id", existing.Id);
            updateCmd.ExecuteNonQuery();

            var updated = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);
            return new ReviewClaimResult(
                Ok: true,
                Action: "claim",
                WorkspaceId: workspaceId,
                Claimed: true,
                Reclaimed: true,
                PreviousHolder: existing.Agent,
                PreviousStatus: existing.Status,
                PrNumber: prNumber,
                HeadSha: headSha,
                Role: role,
                Status: "claimed",
                Claim: updated);
        });
    }

    private ReviewClaimResult CompleteReviewSlot(ReviewClaimRequest request, string workspaceId)
    {
        var (prNumber, headSha, role) = ValidateReviewClaimTarget(request);
        var agent = string.IsNullOrWhiteSpace(request.Agent) ? null : request.Agent.Trim();

        return ExecuteInImmediateTransaction(connection =>
        {
            var completedAtIso = FormatUtc(_timeProvider.GetUtcNow());
            var existing = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);

            if (existing is null)
            {
                if (agent is null)
                {
                    throw new InvalidOperationException($"No review claim found for PR #{prNumber} ({headSha}, {role}).");
                }

                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = """
                    INSERT INTO review_claims (workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc)
                    VALUES (@workspaceId, @prNumber, @headSha, @role, @agent, @completedAt, 'completed', NULL, @completedAt)
                    """;
                insertCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
                insertCmd.Parameters.AddWithValue("@prNumber", prNumber);
                insertCmd.Parameters.AddWithValue("@headSha", headSha);
                insertCmd.Parameters.AddWithValue("@role", role);
                insertCmd.Parameters.AddWithValue("@agent", agent);
                insertCmd.Parameters.AddWithValue("@completedAt", completedAtIso);
                insertCmd.ExecuteNonQuery();
            }
            else
            {
                using var updateCmd = connection.CreateCommand();
                updateCmd.CommandText = """
                    UPDATE review_claims
                    SET head_sha = @headSha,
                        agent = COALESCE(@agent, agent),
                        status = 'completed',
                        expires_at_utc = NULL,
                        completed_at_utc = @completedAt
                    WHERE id = @id
                    """;
                updateCmd.Parameters.AddWithValue("@headSha", headSha);
                updateCmd.Parameters.AddWithValue("@agent", (object?)agent ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@completedAt", completedAtIso);
                updateCmd.Parameters.AddWithValue("@id", existing.Id);
                updateCmd.ExecuteNonQuery();
            }

            var claim = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);
            return new ReviewClaimResult(
                Ok: true,
                Action: "complete",
                WorkspaceId: workspaceId,
                PrNumber: prNumber,
                HeadSha: headSha,
                Role: role,
                Status: "completed",
                Claim: claim);
        });
    }

    private ReviewClaimResult ReleaseReviewSlot(ReviewClaimRequest request, string workspaceId)
    {
        var (prNumber, headSha, role) = ValidateReviewClaimTarget(request);

        return ExecuteInImmediateTransaction(connection =>
        {
            var existing = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);
            if (existing is null)
            {
                return new ReviewClaimResult(
                    Ok: true,
                    Action: "release",
                    WorkspaceId: workspaceId,
                    Released: false,
                    PrNumber: prNumber,
                    HeadSha: headSha,
                    Role: role);
            }

            if (string.Equals(existing.Status, "completed", StringComparison.Ordinal))
            {
                return new ReviewClaimResult(
                    Ok: true,
                    Action: "release",
                    WorkspaceId: workspaceId,
                    Released: false,
                    Reason: "already_completed",
                    PrNumber: prNumber,
                    HeadSha: headSha,
                    Role: role,
                    Status: "completed",
                    Claim: existing);
            }

            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = """
                UPDATE review_claims
                SET head_sha = @headSha,
                    status = 'released',
                    expires_at_utc = NULL,
                    completed_at_utc = NULL
                WHERE id = @id
                """;
            updateCmd.Parameters.AddWithValue("@headSha", headSha);
            updateCmd.Parameters.AddWithValue("@id", existing.Id);
            updateCmd.ExecuteNonQuery();

            var updated = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);
            return new ReviewClaimResult(
                Ok: true,
                Action: "release",
                WorkspaceId: workspaceId,
                Released: true,
                PrNumber: prNumber,
                HeadSha: headSha,
                Role: role,
                Status: "released",
                Claim: updated);
        });
    }

    private ReviewClaimResult ListReviewSlots(ReviewClaimRequest request)
    {
        var allWorkspaces = string.Equals(request.WorkspaceId?.Trim(), "*", StringComparison.Ordinal);
        var normalizedWorkspace = allWorkspaces ? "*" : PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);

        if (request.PrNumber.HasValue && request.PrNumber.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "bus_review_claim list \"pr_number\" must be a positive integer.");
        }

        var headSha = NormalizeHeadSha(request.HeadSha, required: false);
        string? role = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            role = request.Role.Trim().ToLowerInvariant();
            if (!SwarmBusConstants.ValidReviewRoles.Contains(role))
            {
                throw new ArgumentException($"bus_review_claim list \"role\" must be one of: {string.Join(", ", SwarmBusConstants.ValidReviewRoles)}.", nameof(request));
            }
        }

        var claims = ExecuteRead(connection =>
        {
            using var cmd = connection.CreateCommand();
            var clauses = new List<string>();
            if (!allWorkspaces)
            {
                clauses.Add("workspace_id = @workspaceId");
                cmd.Parameters.AddWithValue("@workspaceId", normalizedWorkspace);
            }

            if (request.PrNumber.HasValue)
            {
                clauses.Add("pr_number = @prNumber");
                cmd.Parameters.AddWithValue("@prNumber", request.PrNumber.Value);
            }

            if (headSha is not null)
            {
                clauses.Add("lower(head_sha) = @headSha");
                cmd.Parameters.AddWithValue("@headSha", headSha);
            }

            if (role is not null)
            {
                clauses.Add("role = @role");
                cmd.Parameters.AddWithValue("@role", role);
            }

            var whereSql = clauses.Count > 0 ? $"WHERE {string.Join(" AND ", clauses)}" : string.Empty;
            cmd.CommandText = $"""
                SELECT id, workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc
                FROM review_claims
                {whereSql}
                ORDER BY id DESC
                """;

            var results = new List<ReviewClaim>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(MapReviewClaim(reader));
            }

            return results;
        });

        return new ReviewClaimResult(
            Ok: true,
            Action: "list",
            WorkspaceId: normalizedWorkspace,
            Claims: claims);
    }

    private enum SendTxMode
    {
        Inserted,
        Deduplicated,
        RetryWake
    }

    private sealed record SendTxResult(
        SendTxMode Mode,
        long Id,
        DateTimeOffset CreatedAt,
        string? Body = null,
        string? WakeState = null,
        bool WakeInProgress = false);

    private SendTxResult SendOrDeduplicateInTx(
        SqliteConnection connection,
        string workspaceId,
        string sender,
        string recipient,
        string topic,
        string body,
        int? prNumber,
        int? issueNumber,
        long? replyToId,
        string? state,
        string priority,
        bool wakeTargetMatches)
    {
        var now = _timeProvider.GetUtcNow();
        var createdAtIso = FormatUtc(now);
        var wakeExpiresAt = now.AddSeconds(SwarmBusConstants.DefaultWakeAttemptTtlSeconds);
        var wakeExpiresIso = FormatUtc(wakeExpiresAt);

        if (state is not null)
        {
            using var latestCmd = connection.CreateCommand();
            latestCmd.CommandText = """
                SELECT id, body, status, state, wake_ok, wake_state, wake_attempt_expires_at_utc
                FROM messages
                WHERE workspace_id = @workspaceId
                  AND lower(sender) = lower(@sender)
                  AND lower(recipient) = lower(@recipient)
                  AND topic = @topic
                  AND COALESCE(pr_number, -1) = COALESCE(@prNumber, -1)
                  AND COALESCE(issue_number, -1) = COALESCE(@issueNumber, -1)
                ORDER BY id DESC
                LIMIT 1
                """;
            latestCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
            latestCmd.Parameters.AddWithValue("@sender", sender);
            latestCmd.Parameters.AddWithValue("@recipient", recipient);
            latestCmd.Parameters.AddWithValue("@topic", topic);
            latestCmd.Parameters.AddWithValue("@prNumber", (object?)prNumber ?? DBNull.Value);
            latestCmd.Parameters.AddWithValue("@issueNumber", (object?)issueNumber ?? DBNull.Value);

            using var reader = latestCmd.ExecuteReader();
            if (reader.Read())
            {
                var latestId = reader.GetInt64(0);
                var latestBody = reader.IsDBNull(1) ? body : reader.GetString(1);
                var latestStatus = reader.IsDBNull(2) ? "unread" : reader.GetString(2);
                var latestState = reader.IsDBNull(3) ? null : reader.GetString(3);
                int? latestWakeOk = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                var latestWakeState = reader.IsDBNull(5) ? null : reader.GetString(5);
                var latestWakeExpires = reader.IsDBNull(6) ? null : ParseUtcNullable(reader.GetString(6));
                reader.Close();

                if (string.Equals(latestState, state, StringComparison.Ordinal))
                {
                    return ResolveMatchedStateRow(
                        connection,
                        latestId,
                        latestBody,
                        latestStatus,
                        latestWakeOk,
                        latestWakeState,
                        latestWakeExpires,
                        body,
                        wakeTargetMatches,
                        now,
                        wakeExpiresIso);
                }
            }
        }

        if (sender.Equals("watchdog", StringComparison.OrdinalIgnoreCase) && prNumber.HasValue)
        {
            using var supersedeCmd = connection.CreateCommand();
            supersedeCmd.CommandText = """
                UPDATE messages
                SET status = 'acked', acked_by = 'auto-superseded', acked_at_utc = @ackedAt
                WHERE workspace_id = @workspaceId
                  AND lower(sender) = 'watchdog'
                  AND pr_number = @prNumber
                  AND status = 'unread'
                """;
            supersedeCmd.Parameters.AddWithValue("@ackedAt", createdAtIso);
            supersedeCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
            supersedeCmd.Parameters.AddWithValue("@prNumber", prNumber.Value);
            supersedeCmd.ExecuteNonQuery();
        }

        var isHeadless = SwarmBusConstants.HeadlessRecipients.Contains(recipient);
        var initialStatus = isHeadless ? "acked" : "unread";
        var initialAckedBy = isHeadless ? "auto-headless" : null;
        var initialAckedAt = isHeadless ? createdAtIso : null;
        int? initialWakeOk = wakeTargetMatches ? 0 : null;
        var initialWakeState = wakeTargetMatches ? "pending" : null;
        var initialWakeExpires = wakeTargetMatches ? wakeExpiresIso : null;

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO messages (
                workspace_id, created_at_utc, sender, recipient, topic, body,
                pr_number, issue_number, priority, reply_to_id, status,
                acked_by, acked_at_utc, state, wake_ok, wake_state, wake_attempt_expires_at_utc)
            VALUES (
                @workspaceId, @createdAt, @sender, @recipient, @topic, @body,
                @prNumber, @issueNumber, @priority, @replyToId, @status,
                @ackedBy, @ackedAt, @state, @wakeOk, @wakeState, @wakeExpires);
            SELECT last_insert_rowid();
            """;
        insertCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        insertCmd.Parameters.AddWithValue("@createdAt", createdAtIso);
        insertCmd.Parameters.AddWithValue("@sender", sender);
        insertCmd.Parameters.AddWithValue("@recipient", recipient);
        insertCmd.Parameters.AddWithValue("@topic", topic);
        insertCmd.Parameters.AddWithValue("@body", body);
        insertCmd.Parameters.AddWithValue("@prNumber", (object?)prNumber ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@issueNumber", (object?)issueNumber ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@priority", priority);
        insertCmd.Parameters.AddWithValue("@replyToId", (object?)replyToId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@status", initialStatus);
        insertCmd.Parameters.AddWithValue("@ackedBy", (object?)initialAckedBy ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@ackedAt", (object?)initialAckedAt ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@state", (object?)state ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@wakeOk", (object?)initialWakeOk ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@wakeState", (object?)initialWakeState ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@wakeExpires", (object?)initialWakeExpires ?? DBNull.Value);

        var id = Convert.ToInt64(insertCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        return new SendTxResult(SendTxMode.Inserted, id, now);
    }

    private static SendTxResult ResolveMatchedStateRow(
        SqliteConnection connection,
        long latestId,
        string latestBody,
        string latestStatus,
        int? latestWakeOk,
        string? latestWakeState,
        DateTimeOffset? latestWakeExpires,
        string fallbackBody,
        bool wakeTargetMatches,
        DateTimeOffset now,
        string wakeExpiresIso)
    {
        if (!wakeTargetMatches)
        {
            return new SendTxResult(SendTxMode.Deduplicated, latestId, now, WakeState: latestWakeState);
        }

        var curWakeState = latestWakeState;
        if (string.IsNullOrEmpty(curWakeState))
        {
            curWakeState = latestWakeOk switch
            {
                1 => "ok",
                0 => "failed",
                _ => null
            };
        }

        if (string.Equals(curWakeState, "ok", StringComparison.Ordinal))
        {
            return new SendTxResult(SendTxMode.Deduplicated, latestId, now, WakeState: "ok");
        }

        var hasUnexpiredPending = string.Equals(curWakeState, "pending", StringComparison.Ordinal) &&
                                  latestWakeExpires.HasValue &&
                                  latestWakeExpires.Value > now;
        if (hasUnexpiredPending)
        {
            return new SendTxResult(SendTxMode.Deduplicated, latestId, now, WakeState: "pending", WakeInProgress: true);
        }

        if (string.Equals(latestStatus, "unread", StringComparison.Ordinal))
        {
            using var reserveCmd = connection.CreateCommand();
            reserveCmd.CommandText = """
                UPDATE messages
                SET wake_state = 'pending',
                    wake_attempt_expires_at_utc = @wakeExpires,
                    wake_ok = 0
                WHERE id = @id
                """;
            reserveCmd.Parameters.AddWithValue("@wakeExpires", wakeExpiresIso);
            reserveCmd.Parameters.AddWithValue("@id", latestId);
            reserveCmd.ExecuteNonQuery();

            return new SendTxResult(
                SendTxMode.RetryWake,
                latestId,
                now,
                Body: string.IsNullOrWhiteSpace(latestBody) ? fallbackBody : latestBody);
        }

        return new SendTxResult(SendTxMode.Deduplicated, latestId, now, WakeState: curWakeState);
    }

    private async Task<SwarmWakeResult> DispatchWakeAsync(
        long messageId,
        string workspaceId,
        string sender,
        string recipient,
        int? prNumber,
        string topic,
        string body,
        string? codexThreadId,
        CancellationToken cancellationToken)
    {
        var prTag = prNumber.HasValue ? $" (PR #{prNumber.Value})" : string.Empty;
        var wakeMsg = $"[SwarmBus #{messageId}] {sender} -> {recipient}{prTag} [{topic}]: {body}";
        var context = new SwarmWakeContext(
            MessageId: messageId,
            WorkspaceId: workspaceId,
            Sender: sender,
            Recipient: recipient,
            PrNumber: prNumber,
            Topic: topic,
            Body: body,
            CodexThreadId: codexThreadId,
            FormattedMessage: wakeMsg);

        SwarmWakeResult wakeRes;
        try
        {
            wakeRes = await _wakeDispatcher!(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            wakeRes = new SwarmWakeResult(Attempted: true, Ok: false, Target: recipient, ThreadId: codexThreadId, Error: ex.Message);
        }

        CompleteWakeAttempt(messageId, wakeRes.Ok);
        return wakeRes;
    }

    private static (int PrNumber, string HeadSha, string Role) ValidateReviewClaimTarget(ReviewClaimRequest request)
    {
        if (!request.PrNumber.HasValue || request.PrNumber.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "bus_review_claim requires positive integer \"pr_number\".");
        }

        var headSha = NormalizeHeadSha(request.HeadSha, required: true)!;
        var role = (request.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (!SwarmBusConstants.ValidReviewRoles.Contains(role))
        {
            throw new ArgumentException(
                $"bus_review_claim requires \"role\" to be one of: {string.Join(", ", SwarmBusConstants.ValidReviewRoles)}.",
                nameof(request));
        }

        return (request.PrNumber.Value, headSha, role);
    }

    private static string? NormalizeHeadSha(string? rawSha, bool required)
    {
        var trimmed = (rawSha ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                throw new ArgumentException("bus_review_claim requires a 40-character hexadecimal \"head_sha\".", nameof(rawSha));
            }

            return null;
        }

        if (!HeadShaRegex().IsMatch(trimmed))
        {
            throw new ArgumentException("bus_review_claim \"head_sha\" must be a 40-character hexadecimal SHA.", nameof(rawSha));
        }

        return trimmed.ToLowerInvariant();
    }

    private static string NormalizePriority(string? rawPriority)
    {
        var candidate = (rawPriority ?? "normal").Trim().ToLowerInvariant();
        return SwarmBusConstants.ValidPriorities.Contains(candidate) ? candidate : "normal";
    }

    private static string RequireNonEmpty(string? value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(errorMessage);
        }

        return value.Trim();
    }

    private void PruneExpiredLocksInTx(SqliteConnection connection, DateTimeOffset now)
    {
        var expiredIds = new List<long>();
        using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = "SELECT id, expires_at_utc FROM locks";
            using var reader = selectCmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var rawExpires = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var expiresAt = ParseUtcNullable(rawExpires) ?? DateTimeOffset.MinValue;
                if (expiresAt <= now)
                {
                    expiredIds.Add(id);
                }
            }
        }

        foreach (var id in expiredIds)
        {
            using var delCmd = connection.CreateCommand();
            delCmd.CommandText = "DELETE FROM locks WHERE id = @id";
            delCmd.Parameters.AddWithValue("@id", id);
            delCmd.ExecuteNonQuery();
        }
    }

    private static List<PathLockLease> ReadLocksInWorkspace(SqliteConnection connection, string workspaceId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc
            FROM locks
            WHERE workspace_id = @workspaceId
            ORDER BY id ASC
            """;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        return ReadLocksFromCommand(cmd);
    }

    private static List<PathLockLease> ReadAllLocks(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc
            FROM locks
            ORDER BY id ASC
            """;
        return ReadLocksFromCommand(cmd);
    }

    private static List<PathLockLease> ReadLocksFromCommand(SqliteCommand cmd)
    {
        var locks = new List<PathLockLease>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            locks.Add(new PathLockLease(
                Id: reader.GetInt64(0),
                Agent: reader.GetString(2),
                PathPattern: reader.GetString(3),
                PrNumber: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ClaimedAtUtc: ParseUtcOrMin(reader.GetString(5)),
                ExpiresAtUtc: ParseUtcOrMin(reader.GetString(6)),
                WorkspaceId: reader.IsDBNull(1) ? SwarmBusConstants.DefaultWorkspaceId : reader.GetString(1)));
        }

        return locks;
    }

    private static List<SwarmMessageEnvelope> ReadMessages(SqliteCommand cmd)
    {
        var messages = new List<SwarmMessageEnvelope>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            bool? wakeOk = reader.IsDBNull(15) ? null : reader.GetInt32(15) != 0;
            messages.Add(new SwarmMessageEnvelope(
                Id: reader.GetInt64(0),
                CreatedAtUtc: ParseUtcOrMin(reader.GetString(2)),
                Sender: reader.GetString(3),
                Recipient: reader.GetString(4),
                Topic: reader.GetString(5),
                Body: reader.GetString(6),
                PrNumber: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                IssueNumber: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Priority: reader.IsDBNull(9) ? "normal" : reader.GetString(9),
                ReplyToId: reader.IsDBNull(10) ? null : reader.GetInt64(10),
                Status: reader.IsDBNull(11) ? "unread" : reader.GetString(11),
                AckedBy: reader.IsDBNull(12) ? null : reader.GetString(12),
                AckedAtUtc: reader.IsDBNull(13) ? null : ParseUtcNullable(reader.GetString(13)),
                State: reader.IsDBNull(14) ? null : reader.GetString(14),
                WakeOk: wakeOk,
                WakeState: reader.IsDBNull(16) ? null : reader.GetString(16),
                WakeAttemptExpiresAtUtc: reader.IsDBNull(17) ? null : ParseUtcNullable(reader.GetString(17)),
                WorkspaceId: reader.IsDBNull(1) ? SwarmBusConstants.DefaultWorkspaceId : reader.GetString(1)));
        }

        return messages;
    }

    private static ReviewClaim? ReadReviewClaim(SqliteConnection connection, string workspaceId, int prNumber, string headSha, string role)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc
            FROM review_claims
            WHERE workspace_id = @workspaceId
              AND pr_number = @prNumber
              AND lower(head_sha) = @headSha
              AND role = @role
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@prNumber", prNumber);
        cmd.Parameters.AddWithValue("@headSha", headSha);
        cmd.Parameters.AddWithValue("@role", role);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapReviewClaim(reader) : null;
    }

    private static ReviewClaim MapReviewClaim(SqliteDataReader reader)
    {
        return new ReviewClaim(
            Id: reader.GetInt64(0),
            PrNumber: reader.GetInt32(2),
            HeadSha: reader.GetString(3).ToLowerInvariant(),
            Role: reader.GetString(4),
            Agent: reader.GetString(5),
            ClaimedAtUtc: ParseUtcOrMin(reader.GetString(6)),
            Status: reader.IsDBNull(7) ? "claimed" : reader.GetString(7),
            ExpiresAtUtc: reader.IsDBNull(8) ? null : ParseUtcNullable(reader.GetString(8)),
            CompletedAtUtc: reader.IsDBNull(9) ? null : ParseUtcNullable(reader.GetString(9)),
            WorkspaceId: reader.IsDBNull(1) ? SwarmBusConstants.DefaultWorkspaceId : reader.GetString(1));
    }

    private static WorkQueueItem? ReadWorkQueueItem(SqliteConnection connection, string workspaceId, int issueNumber)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, issue_number, title, priority, risk_class, status,
                   claimed_by, claimed_at_utc, lease_expires_utc, updated_at_utc
            FROM work_queue
            WHERE workspace_id = @workspaceId AND issue_number = @issueNumber
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@issueNumber", issueNumber);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapWorkQueueItem(reader) : null;
    }

    private static WorkQueueItem? ReadWorkQueueItemById(SqliteConnection connection, long id)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, issue_number, title, priority, risk_class, status,
                   claimed_by, claimed_at_utc, lease_expires_utc, updated_at_utc
            FROM work_queue
            WHERE id = @id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapWorkQueueItem(reader) : null;
    }

    private static List<WorkQueueItem> ReadWorkQueueItems(SqliteConnection connection, string workspaceId, string? statusFilter)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = statusFilter is not null
            ? """
              SELECT id, workspace_id, issue_number, title, priority, risk_class, status,
                     claimed_by, claimed_at_utc, lease_expires_utc, updated_at_utc
              FROM work_queue
              WHERE workspace_id = @workspaceId AND status = @status
              ORDER BY priority ASC, id ASC
              """
            : """
              SELECT id, workspace_id, issue_number, title, priority, risk_class, status,
                     claimed_by, claimed_at_utc, lease_expires_utc, updated_at_utc
              FROM work_queue
              WHERE workspace_id = @workspaceId
              ORDER BY priority ASC, id ASC
              """;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        if (statusFilter is not null)
        {
            cmd.Parameters.AddWithValue("@status", statusFilter);
        }

        var items = new List<WorkQueueItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapWorkQueueItem(reader));
        }

        return items;
    }

    private static WorkQueueItem MapWorkQueueItem(SqliteDataReader reader)
    {
        return new WorkQueueItem(
            Id: reader.GetInt64(0),
            IssueNumber: reader.GetInt32(2),
            Title: reader.GetString(3),
            Priority: reader.GetInt32(4),
            RiskClass: reader.IsDBNull(5) ? null : reader.GetString(5),
            Status: reader.GetString(6),
            ClaimedBy: reader.IsDBNull(7) ? null : reader.GetString(7),
            ClaimedAtUtc: reader.IsDBNull(8) ? null : ParseUtcNullable(reader.GetString(8)),
            LeaseExpiresUtc: reader.IsDBNull(9) ? null : ParseUtcNullable(reader.GetString(9)),
            UpdatedAtUtc: ParseUtcOrMin(reader.GetString(10)),
            WorkspaceId: reader.IsDBNull(1) ? SwarmBusConstants.DefaultWorkspaceId : reader.GetString(1));
    }

    private static Dictionary<string, string> ReadMetaMap(SqliteConnection connection, string workspaceId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT key, value
            FROM meta
            WHERE workspace_id = @workspaceId
            ORDER BY key ASC
            """;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.GetString(1);
        }

        return map;
    }

    private static int ReadMetaInt(SqliteConnection connection, string workspaceId, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE workspace_id = @workspaceId AND key = @key LIMIT 1";
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@key", key);
        var raw = cmd.ExecuteScalar() as string;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static void MigrateAndCreateSchema(SqliteConnection connection)
    {
        // 1. Messages: create or additively upgrade columns.
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS messages (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              workspace_id TEXT NOT NULL DEFAULT 'default',
              created_at_utc TEXT NOT NULL,
              sender TEXT NOT NULL,
              recipient TEXT NOT NULL,
              topic TEXT NOT NULL,
              body TEXT NOT NULL,
              pr_number INTEGER,
              issue_number INTEGER,
              priority TEXT NOT NULL DEFAULT 'normal',
              reply_to_id INTEGER,
              status TEXT NOT NULL DEFAULT 'unread',
              acked_by TEXT,
              acked_at_utc TEXT,
              state TEXT,
              wake_ok INTEGER,
              wake_state TEXT CHECK(wake_state IN ('pending','ok','failed')),
              wake_attempt_expires_at_utc TEXT
            );
            """);

        EnsureTableColumns(connection, "messages", [
            ("workspace_id", "workspace_id TEXT NOT NULL DEFAULT 'default'"),
            ("state", "state TEXT"),
            ("wake_ok", "wake_ok INTEGER"),
            ("wake_state", "wake_state TEXT"),
            ("wake_attempt_expires_at_utc", "wake_attempt_expires_at_utc TEXT")
        ]);

        ExecuteNonQuery(connection, """
            CREATE INDEX IF NOT EXISTS idx_messages_recipient_status
              ON messages (recipient, status, id DESC);
            CREATE INDEX IF NOT EXISTS idx_messages_workspace_recipient_status
              ON messages (workspace_id, recipient, status, id DESC);
            """);

        // 2. Locks: create or migrate legacy single-repo UNIQUE(path_pattern) to UNIQUE(workspace_id, path_pattern).
        if (!TableExists(connection, "locks"))
        {
            ExecuteNonQuery(connection, """
                CREATE TABLE locks (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  workspace_id TEXT NOT NULL DEFAULT 'default',
                  agent TEXT NOT NULL,
                  path_pattern TEXT NOT NULL,
                  pr_number INTEGER,
                  claimed_at_utc TEXT NOT NULL,
                  expires_at_utc TEXT NOT NULL,
                  UNIQUE(workspace_id, path_pattern)
                );
                """);
        }
        else if (!GetTableColumns(connection, "locks").Contains("workspace_id"))
        {
            ExecuteNonQuery(connection, """
                CREATE TABLE locks_migrated (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  workspace_id TEXT NOT NULL DEFAULT 'default',
                  agent TEXT NOT NULL,
                  path_pattern TEXT NOT NULL,
                  pr_number INTEGER,
                  claimed_at_utc TEXT NOT NULL,
                  expires_at_utc TEXT NOT NULL,
                  UNIQUE(workspace_id, path_pattern)
                );
                INSERT INTO locks_migrated (id, workspace_id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
                SELECT id, 'default', agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc
                FROM locks;
                DROP TABLE locks;
                ALTER TABLE locks_migrated RENAME TO locks;
                """);
        }

        ExecuteNonQuery(connection, """
            CREATE INDEX IF NOT EXISTS idx_locks_expires
              ON locks (expires_at_utc);
            CREATE INDEX IF NOT EXISTS idx_locks_workspace
              ON locks (workspace_id, expires_at_utc);
            """);

        // 3. Review claims: create or migrate to workspace-scoped unique slot.
        if (!TableExists(connection, "review_claims"))
        {
            ExecuteNonQuery(connection, """
                CREATE TABLE review_claims (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  workspace_id TEXT NOT NULL DEFAULT 'default',
                  pr_number INTEGER NOT NULL,
                  head_sha TEXT NOT NULL,
                  role TEXT NOT NULL,
                  agent TEXT NOT NULL,
                  claimed_at_utc TEXT NOT NULL,
                  status TEXT NOT NULL DEFAULT 'claimed' CHECK(status IN ('claimed','completed','released')),
                  expires_at_utc TEXT,
                  completed_at_utc TEXT,
                  UNIQUE(workspace_id, pr_number, head_sha, role)
                );
                """);
        }
        else
        {
            EnsureTableColumns(connection, "review_claims", [
                ("status", "status TEXT NOT NULL DEFAULT 'claimed'"),
                ("expires_at_utc", "expires_at_utc TEXT"),
                ("completed_at_utc", "completed_at_utc TEXT")
            ]);

            if (!GetTableColumns(connection, "review_claims").Contains("workspace_id"))
            {
                ExecuteNonQuery(connection, """
                    CREATE TABLE review_claims_migrated (
                      id INTEGER PRIMARY KEY AUTOINCREMENT,
                      workspace_id TEXT NOT NULL DEFAULT 'default',
                      pr_number INTEGER NOT NULL,
                      head_sha TEXT NOT NULL,
                      role TEXT NOT NULL,
                      agent TEXT NOT NULL,
                      claimed_at_utc TEXT NOT NULL,
                      status TEXT NOT NULL DEFAULT 'claimed' CHECK(status IN ('claimed','completed','released')),
                      expires_at_utc TEXT,
                      completed_at_utc TEXT,
                      UNIQUE(workspace_id, pr_number, head_sha, role)
                    );
                    INSERT INTO review_claims_migrated (id, workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc)
                    SELECT id, 'default', pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc
                    FROM review_claims;
                    DROP TABLE review_claims;
                    ALTER TABLE review_claims_migrated RENAME TO review_claims;
                    """);
            }
        }

        // 4. Work queue: create or migrate to workspace-scoped unique issue_number.
        if (!TableExists(connection, "work_queue"))
        {
            ExecuteNonQuery(connection, """
                CREATE TABLE work_queue (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  workspace_id TEXT NOT NULL DEFAULT 'default',
                  issue_number INTEGER NOT NULL,
                  title TEXT NOT NULL,
                  priority INTEGER NOT NULL,
                  risk_class TEXT,
                  status TEXT NOT NULL CHECK(status IN ('queued','claimed','done','removed')),
                  claimed_by TEXT,
                  claimed_at_utc TEXT,
                  lease_expires_utc TEXT,
                  updated_at_utc TEXT NOT NULL,
                  UNIQUE(workspace_id, issue_number)
                );
                """);
        }
        else if (!GetTableColumns(connection, "work_queue").Contains("workspace_id"))
        {
            ExecuteNonQuery(connection, """
                DROP INDEX IF EXISTS idx_work_queue_status_priority;
                CREATE TABLE work_queue_migrated (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  workspace_id TEXT NOT NULL DEFAULT 'default',
                  issue_number INTEGER NOT NULL,
                  title TEXT NOT NULL,
                  priority INTEGER NOT NULL,
                  risk_class TEXT,
                  status TEXT NOT NULL CHECK(status IN ('queued','claimed','done','removed')),
                  claimed_by TEXT,
                  claimed_at_utc TEXT,
                  lease_expires_utc TEXT,
                  updated_at_utc TEXT NOT NULL,
                  UNIQUE(workspace_id, issue_number)
                );
                INSERT INTO work_queue_migrated (id, workspace_id, issue_number, title, priority, risk_class, status, claimed_by, claimed_at_utc, lease_expires_utc, updated_at_utc)
                SELECT id, 'default', issue_number, title, priority, risk_class, status, claimed_by, claimed_at_utc, lease_expires_utc, updated_at_utc
                FROM work_queue;
                DROP TABLE work_queue;
                ALTER TABLE work_queue_migrated RENAME TO work_queue;
                """);
        }

        ExecuteNonQuery(connection, """
            CREATE INDEX IF NOT EXISTS idx_work_queue_status_priority
              ON work_queue (workspace_id, status, priority ASC, id ASC);
            """);

        // 5. Meta: create or migrate to composite PRIMARY KEY(workspace_id, key).
        if (!TableExists(connection, "meta"))
        {
            ExecuteNonQuery(connection, """
                CREATE TABLE meta (
                  workspace_id TEXT NOT NULL DEFAULT 'default',
                  key TEXT NOT NULL,
                  value TEXT NOT NULL,
                  updated_at_utc TEXT NOT NULL,
                  PRIMARY KEY (workspace_id, key)
                );
                """);
        }
        else if (!GetTableColumns(connection, "meta").Contains("workspace_id"))
        {
            ExecuteNonQuery(connection, """
                CREATE TABLE meta_migrated (
                  workspace_id TEXT NOT NULL DEFAULT 'default',
                  key TEXT NOT NULL,
                  value TEXT NOT NULL,
                  updated_at_utc TEXT NOT NULL,
                  PRIMARY KEY (workspace_id, key)
                );
                INSERT INTO meta_migrated (workspace_id, key, value, updated_at_utc)
                SELECT 'default', key, value, updated_at_utc
                FROM meta;
                DROP TABLE meta;
                ALTER TABLE meta_migrated RENAME TO meta;
                """);
        }
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() is not null;
    }

    private static HashSet<string> GetTableColumns(SqliteConnection connection, string tableName)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            cols.Add(reader.GetString(1));
        }

        return cols;
    }

    private static void EnsureTableColumns(SqliteConnection connection, string tableName, (string Name, string ColumnSql)[] columnDefs)
    {
        var existingCols = GetTableColumns(connection, tableName);
        foreach (var (colName, colSql) in columnDefs)
        {
            if (!existingCols.Contains(colName))
            {
                ExecuteNonQuery(connection, $"ALTER TABLE {tableName} ADD COLUMN {colSql};");
            }
        }
    }

    private T ExecuteRead<T>(Func<SqliteConnection, T> action)
    {
        EnsureInitialized();
        return ExecuteWithBusyRetry(() =>
        {
            using var connection = OpenConnectionCore();
            return action(connection);
        });
    }

    private T ExecuteInImmediateTransaction<T>(Func<SqliteConnection, T> action)
    {
        EnsureInitialized();
        return ExecuteWithBusyRetry(() =>
        {
            using var connection = OpenConnectionCore();
            return RunInImmediateTransactionCore(connection, () => action(connection));
        });
    }

    private SqliteConnection OpenConnectionCore()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            // CRITICAL: always set busy_timeout FIRST before any WAL or write pragma.
            ExecuteNonQuery(connection, "PRAGMA busy_timeout = 5000;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static T RunInImmediateTransactionCore<T>(SqliteConnection connection, Func<T> action)
    {
        ExecuteNonQuery(connection, "BEGIN IMMEDIATE;");
        try
        {
            var result = action();
            ExecuteNonQuery(connection, "COMMIT;");
            return result;
        }
        catch
        {
            try
            {
                ExecuteNonQuery(connection, "ROLLBACK;");
            }
            catch
            {
                // Ignore rollback error if transaction is already closed.
            }

            throw;
        }
    }

    private T ExecuteWithBusyRetry<T>(Func<T> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (SqliteException ex) when (IsBusyOrLocked(ex) && attempt < _maxBusyRetries)
            {
                var baseDelayMs = Math.Min(250, 10 * Math.Pow(1.35, attempt));
                var jitterMs = Random.Shared.Next(1, 20);
                Thread.Sleep((int)baseDelayMs + jitterMs);
            }
        }
    }

    private static bool IsBusyOrLocked(SqliteException ex) =>
        ex.SqliteErrorCode is SqliteBusyErrorCode or SqliteLockedErrorCode;

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string FormatUtc(DateTimeOffset utc) =>
        utc.ToUniversalTime().ToString(IsoFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseUtcNullable(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset ParseUtcOrMin(string? raw) =>
        ParseUtcNullable(raw) ?? DateTimeOffset.MinValue;
}
