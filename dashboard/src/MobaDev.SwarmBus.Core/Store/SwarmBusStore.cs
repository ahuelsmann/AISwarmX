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

    private const string AllWildcard = "*";
    private const string AllRecipient = "all";
    private const string NormalPriority = "normal";
    private const string CodeWipMetaKey = "code_wip";
    private const string ColWorkspaceId = "workspace_id";
    private const string ColStatus = "status";

    private const string StatusUnread = "unread";
    private const string StatusAcked = "acked";
    private const string StatusClaimed = "claimed";
    private const string StatusCompleted = "completed";
    private const string StatusReleased = "released";
    private const string StatusQueued = "queued";
    private const string StatusDone = "done";
    private const string StatusRemoved = "removed";

    private const string WakeStatePending = "pending";
    private const string WakeStateOk = "ok";
    private const string WakeStateFailed = "failed";

    private const string ActionClaim = "claim";
    private const string ActionComplete = "complete";
    private const string ActionRelease = "release";
    private const string ActionList = "list";
    private const string ActionUpsert = "upsert";

    private const string TableMessages = "messages";
    private const string TableLocks = "locks";
    private const string TableReviewClaims = "review_claims";
    private const string TableWorkQueue = "work_queue";
    private const string TableMeta = "meta";

    private const string ParamWorkspaceId = "@workspaceId";
    private const string ParamAllWorkspaces = "@allWorkspaces";
    private const string ParamAllRecipients = "@allRecipients";
    private const string ParamUnreadOnly = "@unreadOnly";
    private const string ParamAgent = "@agent";
    private const string ParamPrNumber = "@prNumber";
    private const string ParamIssueNumber = "@issueNumber";
    private const string ParamClaimedAt = "@claimedAt";
    private const string ParamAckedAt = "@ackedAt";
    private const string ParamExpiresAt = "@expiresAt";
    private const string ParamUpdatedAt = "@updatedAt";
    private const string ParamCompletedAt = "@completedAt";
    private const string ParamHeadSha = "@headSha";
    private const string ParamRole = "@role";
    private const string ParamStatus = "@status";
    private const string ParamId = "@id";
    private const string ParamLimit = "@limit";
    private const string ParamKey = "@key";
    private const string ParamValue = "@value";

    private const string DeleteLockByIdSql = "DELETE FROM locks WHERE id = @id";

    private readonly string _dbPath;
    private readonly TimeProvider _timeProvider;
    private readonly Func<SwarmWakeContext, CancellationToken, Task<SwarmWakeResult>>? _wakeDispatcher;
    private readonly int _maxBusyRetries;
    private readonly Lock _initGate = new();
    private volatile bool _initialized;

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadShaRegex();

    private enum SendTxMode
    {
        Inserted,
        Deduplicated,
        RetryWake
    }

    private sealed record SendTxInput(
        string WorkspaceId,
        string Sender,
        string Recipient,
        string Topic,
        string Body,
        int? PrNumber,
        int? IssueNumber,
        long? ReplyToId,
        string? State,
        string Priority,
        bool WakeTargetMatches);

    private sealed record SendTxResult(
        SendTxMode Mode,
        long Id,
        DateTimeOffset CreatedAt,
        string? Body = null,
        string? WakeState = null,
        bool WakeInProgress = false);

    private sealed record MatchedMessageRow(
        long Id,
        string Body,
        string Status,
        int? WakeOk,
        string? WakeState,
        DateTimeOffset? WakeAttemptExpiresAtUtc);

    private sealed record WakeResolutionContext(
        string FallbackBody,
        bool WakeTargetMatches,
        DateTimeOffset Now,
        string WakeExpiresIso);

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

        var input = BuildSendTxInput(request);
        var txResult = ExecuteInImmediateTransaction(connection => SendOrDeduplicateInTx(connection, input));

        return txResult.Mode switch
        {
            SendTxMode.Deduplicated => new SwarmSendResult(
                Ok: true,
                Id: txResult.Id,
                WorkspaceId: input.WorkspaceId,
                State: input.State,
                Deduplicated: true,
                WakeInProgress: txResult.WakeInProgress,
                WakeState: txResult.WakeState),
            SendTxMode.RetryWake => await HandleRetryWakeAsync(
                txResult,
                input,
                request.CodexThreadId,
                cancellationToken).ConfigureAwait(false),
            _ => await HandleInsertedMessageAsync(
                txResult,
                input,
                request.CodexThreadId,
                cancellationToken).ConfigureAwait(false)
        };
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
            cmd.Parameters.AddWithValue("@wakeState", ok ? WakeStateOk : WakeStateFailed);
            cmd.Parameters.AddWithValue("@wakeOk", ok ? 1 : 0);
            cmd.Parameters.AddWithValue(ParamId, messageId);
            cmd.ExecuteNonQuery();
            return 0;
        });
    }

    public SwarmInboxResult GetInbox(
        string agent = AllRecipient,
        bool unreadOnly = true,
        int limit = 20,
        string? workspaceId = null)
    {
        var normalizedAgent = string.IsNullOrWhiteSpace(agent) ? AllRecipient : agent.Trim();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        var (allWorkspaces, normalizedWorkspace) = ResolveWorkspaceFilter(workspaceId);
        var includeAllRecipients = normalizedAgent.Equals(AllRecipient, StringComparison.OrdinalIgnoreCase) ||
                                   normalizedAgent == AllWildcard;

        var messages = ExecuteRead(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, workspace_id, created_at_utc, sender, recipient, topic, body,
                       pr_number, issue_number, priority, reply_to_id, status,
                       acked_by, acked_at_utc, state, wake_ok, wake_state, wake_attempt_expires_at_utc
                FROM messages
                WHERE (@allWorkspaces = 1 OR workspace_id = @workspaceId)
                  AND (@allRecipients = 1 OR lower(recipient) = lower(@agent) OR lower(recipient) = 'all')
                  AND (@unreadOnly = 0 OR status = 'unread')
                ORDER BY id DESC
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue(ParamAllWorkspaces, allWorkspaces ? 1 : 0);
            cmd.Parameters.AddWithValue(ParamWorkspaceId, normalizedWorkspace);
            cmd.Parameters.AddWithValue(ParamAllRecipients, includeAllRecipients ? 1 : 0);
            cmd.Parameters.AddWithValue(ParamAgent, normalizedAgent);
            cmd.Parameters.AddWithValue(ParamUnreadOnly, unreadOnly ? 1 : 0);
            cmd.Parameters.AddWithValue(ParamLimit, clampedLimit);

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
        string agent = AllRecipient,
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

        var normalizedAgent = RequireNonEmpty(agent, "bus_ack requires non-empty \"agent\".", nameof(agent));
        var trimmedReply = string.IsNullOrWhiteSpace(reply) ? null : reply.Trim();

        var ackInfo = ExecuteInImmediateTransaction(connection =>
            MarkMessageAckedInTx(connection, messageId, normalizedAgent));

        SwarmSendResult? replyResult = null;
        if (trimmedReply is not null)
        {
            replyResult = await SendMessageAsync(new SwarmSendRequest(
                From: normalizedAgent,
                To: ackInfo.OrigSender,
                Topic: $"re:{ackInfo.OrigTopic}",
                Body: trimmedReply,
                PrNumber: ackInfo.PrNumber,
                IssueNumber: ackInfo.IssueNumber,
                ReplyToId: messageId,
                WorkspaceId: ackInfo.WorkspaceId), cancellationToken).ConfigureAwait(false);
        }

        return new SwarmAckResult(
            Ok: true,
            AckedMessageId: messageId,
            AckedBy: normalizedAgent,
            AckedAtUtc: ackInfo.AckedAt,
            ReplyMessage: replyResult);
    }

    public LockClaimResult ClaimLocks(LockClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agent = RequireNonEmpty(request.Agent, "bus_locks claim requires non-empty \"agent\".", nameof(request));
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
            var expiresIso = FormatUtc(now.AddMinutes(ttlMinutes));

            PruneExpiredLocksInTx(connection, now);

            var existingLocks = ReadLocksInWorkspace(connection, workspaceId);
            var conflicts = FindLockConflicts(cleanPaths, existingLocks, agent, workspaceId);
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
                UpsertLockPathInTx(connection, existingLocks, new LockUpsertEntry(
                    WorkspaceId: workspaceId,
                    Agent: agent,
                    PathPattern: reqPath,
                    PrNumber: request.PrNumber,
                    ClaimedAtIso: nowIso,
                    ExpiresAtIso: expiresIso));
            }

            return new LockClaimResult(
                Ok: true,
                WorkspaceId: workspaceId,
                ClaimedPaths: cleanPaths,
                Conflicts: [],
                Locks: ReadLocksInWorkspace(connection, workspaceId));
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
            var idsToDelete = candidates
                .Where(c => MatchesReleaseFilter(c, agent, request.PrNumber, cleanPaths))
                .Select(c => c.Id)
                .ToList();

            DeleteLocksByIdInTx(connection, idsToDelete);

            return new LockReleaseResult(
                Ok: true,
                WorkspaceId: workspaceId,
                RemovedCount: idsToDelete.Count,
                RemainingLocks: ReadLocksInWorkspace(connection, workspaceId));
        });
    }

    public Task<LockReleaseResult> ReleaseLocksAsync(LockReleaseRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReleaseLocks(request));
    }

    public IReadOnlyList<PathLockLease> ListLocks(string? workspaceId = null)
    {
        var (allWorkspaces, normalizedWorkspace) = ResolveWorkspaceFilter(workspaceId);

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

        var action = string.IsNullOrWhiteSpace(request.Action) ? ActionClaim : request.Action.Trim().ToLowerInvariant();
        var workspaceId = PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);

        return action switch
        {
            ActionClaim => ClaimReviewSlot(request, workspaceId),
            ActionComplete => CompleteReviewSlot(request, workspaceId),
            ActionRelease => ReleaseReviewSlot(request, workspaceId),
            ActionList => ListReviewSlots(request),
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
        var incomingStatus = string.IsNullOrWhiteSpace(request.Status) ? StatusQueued : request.Status.Trim().ToLowerInvariant();
        if (!SwarmBusConstants.ValidQueueStatuses.Contains(incomingStatus))
        {
            throw new ArgumentException($"Invalid work_queue status '{incomingStatus}'. Expected: queued, claimed, done, removed.", nameof(request));
        }

        var rawTitle = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        var isTerminalWithoutTitle = rawTitle is null && incomingStatus is StatusDone or StatusRemoved;
        if (rawTitle is null && !isTerminalWithoutTitle)
        {
            throw new ArgumentException("bus_queue upsert requires non-empty \"title\".", nameof(request));
        }

        var priority = request.Priority ?? 2;
        var riskClass = string.IsNullOrWhiteSpace(request.RiskClass) ? null : request.RiskClass.Trim().ToUpperInvariant();

        var result = ExecuteInImmediateTransaction(connection =>
        {
            var nowIso = FormatUtc(_timeProvider.GetUtcNow());
            if (isTerminalWithoutTitle)
            {
                return TryMarkDoneOrRemovedWithoutTitleInTx(connection, workspaceId, request.IssueNumber, incomingStatus, nowIso);
            }

            return UpsertQueueItemInTx(connection, workspaceId, request.IssueNumber, rawTitle!, priority, riskClass, incomingStatus, nowIso);
        });

        if (result is null)
        {
            throw new ArgumentException("bus_queue upsert requires non-empty \"title\".", nameof(request));
        }

        return result;
    }

    public Task<WorkQueueMutationResult> QueueUpsertAsync(WorkQueueUpsertRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueueUpsert(request));
    }

    public WorkQueueNextResult QueueNext(string agent, string? workspaceId = null)
    {
        var normalizedAgent = RequireNonEmpty(agent, "bus_queue next requires non-empty \"agent\".", nameof(agent));
        var normalizedWorkspace = PathPatternMatcher.NormalizeWorkspaceId(workspaceId);

        return ExecuteInImmediateTransaction(connection =>
        {
            var now = _timeProvider.GetUtcNow();
            var nowIso = FormatUtc(now);
            var leaseExpiresIso = FormatUtc(now.AddHours(SwarmBusConstants.DefaultQueueLeaseHours));

            var resumed = TryResumeOwnClaimInTx(connection, normalizedWorkspace, normalizedAgent, now, nowIso, leaseExpiresIso);
            if (resumed is not null)
            {
                return resumed;
            }

            var (baseCodeWip, activeClaimedCodeCount, effectiveCodeWip) = ComputeEffectiveCodeWipInTx(connection, normalizedWorkspace, now);
            var restrictToLowRisk = effectiveCodeWip >= SwarmBusConstants.CodeWipClaimLimit;
            var candidate = SelectNextCandidate(connection, normalizedWorkspace, now, restrictToLowRisk);

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

            var claimed = ClaimQueueItemInTx(connection, candidate.Id, normalizedAgent, nowIso, leaseExpiresIso);
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
            cmd.CommandText = """
                UPDATE work_queue
                SET status = 'queued', claimed_by = NULL, claimed_at_utc = NULL, lease_expires_utc = NULL, updated_at_utc = @updatedAt
                WHERE workspace_id = @workspaceId
                  AND issue_number = @issueNumber
                  AND status = 'claimed'
                  AND (@agent IS NULL OR lower(claimed_by) = lower(@agent))
                """;
            cmd.Parameters.AddWithValue(ParamUpdatedAt, nowIso);
            cmd.Parameters.AddWithValue(ParamWorkspaceId, normalizedWorkspace);
            cmd.Parameters.AddWithValue(ParamIssueNumber, issueNumber);
            cmd.Parameters.AddWithValue(ParamAgent, (object?)normalizedAgent ?? DBNull.Value);

            var changed = cmd.ExecuteNonQuery() > 0;
            return new WorkQueueMutationResult(
                Ok: true,
                Action: ActionRelease,
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
            cmd.Parameters.AddWithValue(ParamUpdatedAt, nowIso);
            cmd.Parameters.AddWithValue(ParamWorkspaceId, normalizedWorkspace);
            cmd.Parameters.AddWithValue(ParamIssueNumber, issueNumber);

            var changed = cmd.ExecuteNonQuery() > 0;
            return new WorkQueueMutationResult(
                Ok: true,
                Action: StatusDone,
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
            var codeWip = meta.TryGetValue(CodeWipMetaKey, out var rawWip) && int.TryParse(rawWip, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
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
        var normalizedKey = RequireNonEmpty(key, "bus_queue sync-meta requires non-empty \"meta_key\".", nameof(key));
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
            cmd.Parameters.AddWithValue(ParamWorkspaceId, normalizedWorkspace);
            cmd.Parameters.AddWithValue(ParamKey, normalizedKey);
            cmd.Parameters.AddWithValue(ParamValue, value);
            cmd.Parameters.AddWithValue(ParamUpdatedAt, nowIso);
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
        var (allWorkspaces, normalizedWorkspace) = ResolveWorkspaceFilter(workspaceId);

        return ExecuteInImmediateTransaction(connection =>
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpiredLocksInTx(connection, now);

            var unreadByRecipient = ReadUnreadByRecipient(connection, allWorkspaces, normalizedWorkspace);
            var totalUnread = ReadTotalUnreadCount(connection, allWorkspaces, normalizedWorkspace);
            var activeLocks = allWorkspaces
                ? ReadAllLocks(connection)
                : ReadLocksInWorkspace(connection, normalizedWorkspace);
            var recentMessages = ReadRecentMessages(connection, allWorkspaces, normalizedWorkspace, clampedLimit);

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

    private sealed record LockUpsertEntry(
        string WorkspaceId,
        string Agent,
        string PathPattern,
        int? PrNumber,
        string ClaimedAtIso,
        string ExpiresAtIso);

    private static SendTxInput BuildSendTxInput(SwarmSendRequest request)
    {
        var sender = RequireNonEmpty(request.From, "bus_send requires non-empty \"from\".", nameof(request));
        var recipient = RequireNonEmpty(request.To, "bus_send requires non-empty \"to\".", nameof(request));
        var topic = RequireNonEmpty(request.Topic, "bus_send requires non-empty \"topic\".", nameof(request));
        var body = RequireNonEmpty(request.Body, "bus_send requires non-empty \"body\".", nameof(request));

        var workspaceId = PathPatternMatcher.NormalizeWorkspaceId(request.WorkspaceId);
        var state = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();
        var priority = NormalizePriority(request.Priority);
        var recLower = recipient.ToLowerInvariant();
        var wakeTargetMatches = request.Wake && (recLower.Contains("codex", StringComparison.Ordinal) || recLower == AllRecipient);

        return new SendTxInput(
            WorkspaceId: workspaceId,
            Sender: sender,
            Recipient: recipient,
            Topic: topic,
            Body: body,
            PrNumber: request.PrNumber,
            IssueNumber: request.IssueNumber,
            ReplyToId: request.ReplyToId,
            State: state,
            Priority: priority,
            WakeTargetMatches: wakeTargetMatches);
    }

    private async Task<SwarmSendResult> HandleRetryWakeAsync(
        SendTxResult txResult,
        SendTxInput input,
        string? rawCodexThreadId,
        CancellationToken cancellationToken)
    {
        if (_wakeDispatcher is null)
        {
            return new SwarmSendResult(
                Ok: true,
                Id: txResult.Id,
                WorkspaceId: input.WorkspaceId,
                State: input.State,
                Deduplicated: true,
                WakeRetried: true,
                WakeInProgress: true,
                WakeState: WakeStatePending);
        }

        var context = BuildWakeContext(txResult.Id, input, txResult.Body ?? input.Body, rawCodexThreadId);
        var wakeRes = await DispatchWakeAsync(context, cancellationToken).ConfigureAwait(false);
        return new SwarmSendResult(
            Ok: true,
            Id: txResult.Id,
            WorkspaceId: input.WorkspaceId,
            State: input.State,
            Deduplicated: true,
            WakeRetried: true,
            WakeState: wakeRes.Ok ? WakeStateOk : WakeStateFailed,
            WakeResults: [wakeRes]);
    }

    private async Task<SwarmSendResult> HandleInsertedMessageAsync(
        SendTxResult txResult,
        SendTxInput input,
        string? rawCodexThreadId,
        CancellationToken cancellationToken)
    {
        List<SwarmWakeResult> wakeResults = [];
        string? finalWakeState = input.WakeTargetMatches ? WakeStatePending : null;

        if (input.WakeTargetMatches && _wakeDispatcher is not null)
        {
            var context = BuildWakeContext(txResult.Id, input, input.Body, rawCodexThreadId);
            var wakeRes = await DispatchWakeAsync(context, cancellationToken).ConfigureAwait(false);
            wakeResults = [wakeRes];
            finalWakeState = wakeRes.Ok ? WakeStateOk : WakeStateFailed;
        }

        return new SwarmSendResult(
            Ok: true,
            Id: txResult.Id,
            WorkspaceId: input.WorkspaceId,
            CreatedAtUtc: txResult.CreatedAt,
            From: input.Sender,
            To: input.Recipient,
            Topic: input.Topic,
            PrNumber: input.PrNumber,
            IssueNumber: input.IssueNumber,
            Priority: input.Priority,
            State: input.State,
            WakeState: finalWakeState,
            WakeResults: wakeResults);
    }

    private static SwarmWakeContext BuildWakeContext(
        long messageId,
        SendTxInput input,
        string effectiveBody,
        string? rawCodexThreadId)
    {
        var codexThreadId = string.IsNullOrWhiteSpace(rawCodexThreadId) ? null : rawCodexThreadId.Trim();
        var prTag = input.PrNumber.HasValue ? $" (PR #{input.PrNumber.Value})" : string.Empty;
        var wakeMsg = $"[SwarmBus #{messageId}] {input.Sender} -> {input.Recipient}{prTag} [{input.Topic}]: {effectiveBody}";

        return new SwarmWakeContext(
            MessageId: messageId,
            WorkspaceId: input.WorkspaceId,
            Sender: input.Sender,
            Recipient: input.Recipient,
            PrNumber: input.PrNumber,
            Topic: input.Topic,
            Body: effectiveBody,
            CodexThreadId: codexThreadId,
            FormattedMessage: wakeMsg);
    }

    private (string WorkspaceId, string OrigSender, string OrigTopic, int? PrNumber, int? IssueNumber, DateTimeOffset AckedAt) MarkMessageAckedInTx(
        SqliteConnection connection,
        long messageId,
        string normalizedAgent)
    {
        using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = """
            SELECT id, workspace_id, sender, topic, pr_number, issue_number
            FROM messages
            WHERE id = @id
            """;
        selectCmd.Parameters.AddWithValue(ParamId, messageId);

        using var reader = selectCmd.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Message #{messageId} not found.");
        }

        var workspaceId = GetStringOrDefault(reader, 1, SwarmBusConstants.DefaultWorkspaceId);
        var origSender = reader.GetString(2);
        var origTopic = reader.GetString(3);
        var prNumber = GetNullableInt32(reader, 4);
        var issueNumber = GetNullableInt32(reader, 5);
        reader.Close();

        var ackedAt = _timeProvider.GetUtcNow();
        using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = """
            UPDATE messages
            SET status = 'acked', acked_by = @agent, acked_at_utc = @ackedAt
            WHERE id = @id
            """;
        updateCmd.Parameters.AddWithValue(ParamAgent, normalizedAgent);
        updateCmd.Parameters.AddWithValue(ParamAckedAt, FormatUtc(ackedAt));
        updateCmd.Parameters.AddWithValue(ParamId, messageId);
        updateCmd.ExecuteNonQuery();

        return (workspaceId, origSender, origTopic, prNumber, issueNumber, ackedAt);
    }

    private static List<LockConflict> FindLockConflicts(
        IReadOnlyList<string> cleanPaths,
        IReadOnlyList<PathLockLease> existingLocks,
        string agent,
        string workspaceId)
    {
        return cleanPaths
            .SelectMany(reqPath => existingLocks
                .Where(ex => !ex.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase) &&
                             PathPatternMatcher.LocksOverlap(workspaceId, reqPath, ex.WorkspaceId, ex.PathPattern))
                .Select(ex => new LockConflict(
                    RequestedPath: reqPath,
                    ConflictingAgent: ex.Agent,
                    ConflictingPath: ex.PathPattern,
                    ConflictingPr: ex.PrNumber,
                    ExpiresAtUtc: ex.ExpiresAtUtc,
                    WorkspaceId: workspaceId)))
            .ToList();
    }

    private static void UpsertLockPathInTx(
        SqliteConnection connection,
        IReadOnlyList<PathLockLease> existingLocks,
        LockUpsertEntry entry)
    {
        var normalizedReq = PathPatternMatcher.NormalizePath(entry.PathPattern);
        var staleIds = existingLocks
            .Where(l => string.Equals(PathPatternMatcher.NormalizePath(l.PathPattern), normalizedReq, StringComparison.Ordinal) &&
                        !string.Equals(l.PathPattern, entry.PathPattern, StringComparison.Ordinal))
            .Select(l => l.Id)
            .ToList();

        DeleteLocksByIdInTx(connection, staleIds);

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
        upsert.Parameters.AddWithValue(ParamWorkspaceId, entry.WorkspaceId);
        upsert.Parameters.AddWithValue(ParamAgent, entry.Agent);
        upsert.Parameters.AddWithValue("@pathPattern", entry.PathPattern);
        upsert.Parameters.AddWithValue(ParamPrNumber, (object?)entry.PrNumber ?? DBNull.Value);
        upsert.Parameters.AddWithValue(ParamClaimedAt, entry.ClaimedAtIso);
        upsert.Parameters.AddWithValue(ParamExpiresAt, entry.ExpiresAtIso);
        upsert.ExecuteNonQuery();
    }

    private static bool MatchesReleaseFilter(
        PathLockLease candidate,
        string? agent,
        int? prNumber,
        HashSet<string> cleanPaths)
    {
        if (agent is not null && !candidate.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (prNumber.HasValue && candidate.PrNumber != prNumber.Value)
        {
            return false;
        }

        return cleanPaths.Count == 0 || cleanPaths.Contains(PathPatternMatcher.NormalizePath(candidate.PathPattern));
    }

    private static void DeleteLocksByIdInTx(SqliteConnection connection, IReadOnlyList<long> ids)
    {
        foreach (var id in ids)
        {
            using var delCmd = connection.CreateCommand();
            delCmd.CommandText = DeleteLockByIdSql;
            delCmd.Parameters.AddWithValue(ParamId, id);
            delCmd.ExecuteNonQuery();
        }
    }

    private static WorkQueueMutationResult? TryMarkDoneOrRemovedWithoutTitleInTx(
        SqliteConnection connection,
        string workspaceId,
        int issueNumber,
        string incomingStatus,
        string nowIso)
    {
        var existing = ReadWorkQueueItem(connection, workspaceId, issueNumber);
        if (existing is null)
        {
            return null;
        }

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
        updateCmd.Parameters.AddWithValue(ParamStatus, incomingStatus);
        updateCmd.Parameters.AddWithValue(ParamUpdatedAt, nowIso);
        updateCmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        updateCmd.Parameters.AddWithValue(ParamIssueNumber, issueNumber);
        updateCmd.ExecuteNonQuery();

        return new WorkQueueMutationResult(
            Ok: true,
            Action: ActionUpsert,
            WorkspaceId: workspaceId,
            IssueNumber: issueNumber,
            Changed: true,
            Item: ReadWorkQueueItem(connection, workspaceId, issueNumber));
    }

    private static WorkQueueMutationResult UpsertQueueItemInTx(
        SqliteConnection connection,
        string workspaceId,
        int issueNumber,
        string title,
        int priority,
        string? riskClass,
        string incomingStatus,
        string nowIso)
    {
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
        upsertCmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        upsertCmd.Parameters.AddWithValue(ParamIssueNumber, issueNumber);
        upsertCmd.Parameters.AddWithValue("@title", title);
        upsertCmd.Parameters.AddWithValue("@priority", priority);
        upsertCmd.Parameters.AddWithValue("@riskClass", (object?)riskClass ?? DBNull.Value);
        upsertCmd.Parameters.AddWithValue(ParamStatus, incomingStatus);
        upsertCmd.Parameters.AddWithValue(ParamUpdatedAt, nowIso);
        upsertCmd.ExecuteNonQuery();

        return new WorkQueueMutationResult(
            Ok: true,
            Action: ActionUpsert,
            WorkspaceId: workspaceId,
            IssueNumber: issueNumber,
            Changed: true,
            Item: ReadWorkQueueItem(connection, workspaceId, issueNumber));
    }

    private static WorkQueueNextResult? TryResumeOwnClaimInTx(
        SqliteConnection connection,
        string workspaceId,
        string normalizedAgent,
        DateTimeOffset now,
        string nowIso,
        string leaseExpiresIso)
    {
        var ownActive = ReadWorkQueueItems(connection, workspaceId, StatusClaimed)
            .Where(item => string.Equals(item.ClaimedBy, normalizedAgent, StringComparison.OrdinalIgnoreCase) &&
                           item.LeaseExpiresUtc.HasValue &&
                           item.LeaseExpiresUtc.Value > now)
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Id)
            .FirstOrDefault();

        if (ownActive is null)
        {
            return null;
        }

        using var renewCmd = connection.CreateCommand();
        renewCmd.CommandText = """
            UPDATE work_queue
            SET lease_expires_utc = @leaseExpires, updated_at_utc = @updatedAt
            WHERE id = @id
            """;
        renewCmd.Parameters.AddWithValue("@leaseExpires", leaseExpiresIso);
        renewCmd.Parameters.AddWithValue(ParamUpdatedAt, nowIso);
        renewCmd.Parameters.AddWithValue(ParamId, ownActive.Id);
        renewCmd.ExecuteNonQuery();

        return new WorkQueueNextResult(
            Ok: true,
            WorkspaceId: workspaceId,
            Resumed: true,
            CodeWip: 0,
            ActiveClaimedCodeCount: 0,
            EffectiveCodeWip: 0,
            Item: ReadWorkQueueItemById(connection, ownActive.Id));
    }

    private static (int BaseCodeWip, int ActiveClaimedCodeCount, int EffectiveCodeWip) ComputeEffectiveCodeWipInTx(
        SqliteConnection connection,
        string workspaceId,
        DateTimeOffset now)
    {
        var baseCodeWip = ReadMetaInt(connection, workspaceId, CodeWipMetaKey);
        var activeClaimedCodeCount = ReadWorkQueueItems(connection, workspaceId, StatusClaimed)
            .Count(item => IsUnexpired(item.LeaseExpiresUtc, now) && !IsLowRisk(item.RiskClass));

        return (baseCodeWip, activeClaimedCodeCount, baseCodeWip + activeClaimedCodeCount);
    }

    private static WorkQueueItem? SelectNextCandidate(
        SqliteConnection connection,
        string workspaceId,
        DateTimeOffset now,
        bool restrictToLowRisk)
    {
        return ReadWorkQueueItems(connection, workspaceId, statusFilter: null)
            .Where(item => IsQueueEligible(item, now) && (!restrictToLowRisk || IsLowRisk(item.RiskClass)))
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
    }

    private static bool IsQueueEligible(WorkQueueItem item, DateTimeOffset now) =>
        string.Equals(item.Status, StatusQueued, StringComparison.Ordinal) ||
        (string.Equals(item.Status, StatusClaimed, StringComparison.Ordinal) &&
         item.LeaseExpiresUtc.HasValue &&
         item.LeaseExpiresUtc.Value <= now);

    private static bool IsUnexpired(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        !expiresAt.HasValue || expiresAt.Value > now;

    private static bool IsLowRisk(string? riskClass) =>
        string.Equals(riskClass?.Trim(), "L", StringComparison.OrdinalIgnoreCase);

    private static WorkQueueItem? ClaimQueueItemInTx(
        SqliteConnection connection,
        long itemId,
        string agent,
        string nowIso,
        string leaseExpiresIso)
    {
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
        claimCmd.Parameters.AddWithValue(ParamAgent, agent);
        claimCmd.Parameters.AddWithValue(ParamClaimedAt, nowIso);
        claimCmd.Parameters.AddWithValue("@leaseExpires", leaseExpiresIso);
        claimCmd.Parameters.AddWithValue(ParamUpdatedAt, nowIso);
        claimCmd.Parameters.AddWithValue(ParamId, itemId);
        claimCmd.ExecuteNonQuery();

        return ReadWorkQueueItemById(connection, itemId);
    }

    private static List<RecipientUnreadCount> ReadUnreadByRecipient(SqliteConnection connection, bool allWorkspaces, string workspaceId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT recipient, COUNT(*) AS unread_count
            FROM messages
            WHERE (@allWorkspaces = 1 OR workspace_id = @workspaceId)
              AND status = 'unread'
            GROUP BY recipient
            ORDER BY unread_count DESC, recipient ASC
            """;
        cmd.Parameters.AddWithValue(ParamAllWorkspaces, allWorkspaces ? 1 : 0);
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);

        var result = new List<RecipientUnreadCount>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new RecipientUnreadCount(
                Recipient: reader.GetString(0),
                UnreadCount: reader.GetInt32(1)));
        }

        return result;
    }

    private static int ReadTotalUnreadCount(SqliteConnection connection, bool allWorkspaces, string workspaceId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM messages
            WHERE (@allWorkspaces = 1 OR workspace_id = @workspaceId)
              AND status = 'unread'
            """;
        cmd.Parameters.AddWithValue(ParamAllWorkspaces, allWorkspaces ? 1 : 0);
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);

        var scalar = cmd.ExecuteScalar();
        return scalar is not null && scalar != DBNull.Value
            ? Convert.ToInt32(scalar, CultureInfo.InvariantCulture)
            : 0;
    }

    private static List<SwarmMessageEnvelope> ReadRecentMessages(
        SqliteConnection connection,
        bool allWorkspaces,
        string workspaceId,
        int limit)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, created_at_utc, sender, recipient, topic, body,
                   pr_number, issue_number, priority, reply_to_id, status,
                   acked_by, acked_at_utc, state, wake_ok, wake_state, wake_attempt_expires_at_utc
            FROM messages
            WHERE (@allWorkspaces = 1 OR workspace_id = @workspaceId)
            ORDER BY id DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue(ParamAllWorkspaces, allWorkspaces ? 1 : 0);
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        cmd.Parameters.AddWithValue(ParamLimit, limit);
        return ReadMessages(cmd);
    }

    private ReviewClaimResult ClaimReviewSlot(ReviewClaimRequest request, string workspaceId)
    {
        var agent = RequireNonEmpty(request.Agent, "bus_review_claim claim requires non-empty \"agent\".", nameof(request));
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
                return InsertNewReviewClaimInTx(connection, workspaceId, prNumber, headSha, role, agent, claimedAtIso, expiresAtIso);
            }

            if (string.Equals(existing.Status, StatusCompleted, StringComparison.Ordinal))
            {
                return CreateBlockedReviewClaimResult(existing, workspaceId, "already_completed", StatusCompleted, alreadyClaimedBySelf: null);
            }

            if (IsActiveReviewClaim(existing, now))
            {
                return CreateBlockedReviewClaimResult(
                    existing,
                    workspaceId,
                    "already_claimed",
                    StatusClaimed,
                    alreadyClaimedBySelf: existing.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            }

            return ReclaimReviewSlotInTx(connection, existing, workspaceId, headSha, agent, claimedAtIso, expiresAtIso);
        });
    }

    private static bool IsActiveReviewClaim(ReviewClaim existing, DateTimeOffset now) =>
        string.Equals(existing.Status, StatusClaimed, StringComparison.Ordinal) &&
        existing.ExpiresAtUtc.HasValue &&
        existing.ExpiresAtUtc.Value > now;

    private static ReviewClaimResult CreateBlockedReviewClaimResult(
        ReviewClaim existing,
        string workspaceId,
        string reason,
        string status,
        bool? alreadyClaimedBySelf) =>
        new(
            Ok: true,
            Action: ActionClaim,
            WorkspaceId: workspaceId,
            Claimed: false,
            Reason: reason,
            AlreadyClaimedBySelf: alreadyClaimedBySelf,
            Holder: existing.Agent,
            PrNumber: existing.PrNumber,
            HeadSha: existing.HeadSha,
            Role: existing.Role,
            Status: status,
            Claim: existing);

    private static ReviewClaimResult InsertNewReviewClaimInTx(
        SqliteConnection connection,
        string workspaceId,
        int prNumber,
        string headSha,
        string role,
        string agent,
        string claimedAtIso,
        string expiresAtIso)
    {
        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO review_claims (workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc)
            VALUES (@workspaceId, @prNumber, @headSha, @role, @agent, @claimedAt, 'claimed', @expiresAt, NULL)
            """;
        insertCmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        insertCmd.Parameters.AddWithValue(ParamPrNumber, prNumber);
        insertCmd.Parameters.AddWithValue(ParamHeadSha, headSha);
        insertCmd.Parameters.AddWithValue(ParamRole, role);
        insertCmd.Parameters.AddWithValue(ParamAgent, agent);
        insertCmd.Parameters.AddWithValue(ParamClaimedAt, claimedAtIso);
        insertCmd.Parameters.AddWithValue(ParamExpiresAt, expiresAtIso);
        insertCmd.ExecuteNonQuery();

        return new ReviewClaimResult(
            Ok: true,
            Action: ActionClaim,
            WorkspaceId: workspaceId,
            Claimed: true,
            Reclaimed: false,
            PrNumber: prNumber,
            HeadSha: headSha,
            Role: role,
            Status: StatusClaimed,
            Claim: ReadReviewClaim(connection, workspaceId, prNumber, headSha, role));
    }

    private static ReviewClaimResult ReclaimReviewSlotInTx(
        SqliteConnection connection,
        ReviewClaim existing,
        string workspaceId,
        string headSha,
        string agent,
        string claimedAtIso,
        string expiresAtIso)
    {
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
        updateCmd.Parameters.AddWithValue(ParamHeadSha, headSha);
        updateCmd.Parameters.AddWithValue(ParamAgent, agent);
        updateCmd.Parameters.AddWithValue(ParamClaimedAt, claimedAtIso);
        updateCmd.Parameters.AddWithValue(ParamExpiresAt, expiresAtIso);
        updateCmd.Parameters.AddWithValue(ParamId, existing.Id);
        updateCmd.ExecuteNonQuery();

        return new ReviewClaimResult(
            Ok: true,
            Action: ActionClaim,
            WorkspaceId: workspaceId,
            Claimed: true,
            Reclaimed: true,
            PreviousHolder: existing.Agent,
            PreviousStatus: existing.Status,
            PrNumber: existing.PrNumber,
            HeadSha: headSha,
            Role: existing.Role,
            Status: StatusClaimed,
            Claim: ReadReviewClaim(connection, workspaceId, existing.PrNumber, headSha, existing.Role));
    }

    private ReviewClaimResult CompleteReviewSlot(ReviewClaimRequest request, string workspaceId)
    {
        var (prNumber, headSha, role) = ValidateReviewClaimTarget(request);
        var agent = string.IsNullOrWhiteSpace(request.Agent) ? null : request.Agent.Trim();

        var existingFound = ExecuteInImmediateTransaction(connection =>
        {
            var completedAtIso = FormatUtc(_timeProvider.GetUtcNow());
            var existing = ReadReviewClaim(connection, workspaceId, prNumber, headSha, role);
            if (existing is null && agent is null)
            {
                return false;
            }

            if (existing is null)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = """
                    INSERT INTO review_claims (workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc)
                    VALUES (@workspaceId, @prNumber, @headSha, @role, @agent, @completedAt, 'completed', NULL, @completedAt)
                    """;
                insertCmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
                insertCmd.Parameters.AddWithValue(ParamPrNumber, prNumber);
                insertCmd.Parameters.AddWithValue(ParamHeadSha, headSha);
                insertCmd.Parameters.AddWithValue(ParamRole, role);
                insertCmd.Parameters.AddWithValue(ParamAgent, agent!);
                insertCmd.Parameters.AddWithValue(ParamCompletedAt, completedAtIso);
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
                updateCmd.Parameters.AddWithValue(ParamHeadSha, headSha);
                updateCmd.Parameters.AddWithValue(ParamAgent, (object?)agent ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue(ParamCompletedAt, completedAtIso);
                updateCmd.Parameters.AddWithValue(ParamId, existing.Id);
                updateCmd.ExecuteNonQuery();
            }

            return true;
        });

        if (!existingFound)
        {
            throw new InvalidOperationException($"No review claim found for PR #{prNumber} ({headSha}, {role}).");
        }

        var claim = ExecuteRead(connection => ReadReviewClaim(connection, workspaceId, prNumber, headSha, role));
        return new ReviewClaimResult(
            Ok: true,
            Action: ActionComplete,
            WorkspaceId: workspaceId,
            PrNumber: prNumber,
            HeadSha: headSha,
            Role: role,
            Status: StatusCompleted,
            Claim: claim);
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
                    Action: ActionRelease,
                    WorkspaceId: workspaceId,
                    Released: false,
                    PrNumber: prNumber,
                    HeadSha: headSha,
                    Role: role);
            }

            if (string.Equals(existing.Status, StatusCompleted, StringComparison.Ordinal))
            {
                return new ReviewClaimResult(
                    Ok: true,
                    Action: ActionRelease,
                    WorkspaceId: workspaceId,
                    Released: false,
                    Reason: "already_completed",
                    PrNumber: prNumber,
                    HeadSha: headSha,
                    Role: role,
                    Status: StatusCompleted,
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
            updateCmd.Parameters.AddWithValue(ParamHeadSha, headSha);
            updateCmd.Parameters.AddWithValue(ParamId, existing.Id);
            updateCmd.ExecuteNonQuery();

            return new ReviewClaimResult(
                Ok: true,
                Action: ActionRelease,
                WorkspaceId: workspaceId,
                Released: true,
                PrNumber: prNumber,
                HeadSha: headSha,
                Role: role,
                Status: StatusReleased,
                Claim: ReadReviewClaim(connection, workspaceId, prNumber, headSha, role));
        });
    }

    private ReviewClaimResult ListReviewSlots(ReviewClaimRequest request)
    {
        var (allWorkspaces, normalizedWorkspace) = ResolveWorkspaceFilter(request.WorkspaceId);

        if (request.PrNumber.HasValue && request.PrNumber.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "bus_review_claim list \"pr_number\" must be a positive integer.");
        }

        var headSha = NormalizeHeadSha(request.HeadSha, required: false, nameof(request));
        var role = NormalizeOptionalReviewRole(request.Role, nameof(request));

        var claims = ExecuteRead(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, workspace_id, pr_number, head_sha, role, agent, claimed_at_utc, status, expires_at_utc, completed_at_utc
                FROM review_claims
                WHERE (@allWorkspaces = 1 OR workspace_id = @workspaceId)
                  AND (@prNumber IS NULL OR pr_number = @prNumber)
                  AND (@headSha IS NULL OR lower(head_sha) = @headSha)
                  AND (@role IS NULL OR role = @role)
                ORDER BY id DESC
                """;
            cmd.Parameters.AddWithValue(ParamAllWorkspaces, allWorkspaces ? 1 : 0);
            cmd.Parameters.AddWithValue(ParamWorkspaceId, normalizedWorkspace);
            cmd.Parameters.AddWithValue(ParamPrNumber, (object?)request.PrNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue(ParamHeadSha, (object?)headSha ?? DBNull.Value);
            cmd.Parameters.AddWithValue(ParamRole, (object?)role ?? DBNull.Value);

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
            Action: ActionList,
            WorkspaceId: normalizedWorkspace,
            Claims: claims);
    }

    private SendTxResult SendOrDeduplicateInTx(SqliteConnection connection, SendTxInput input)
    {
        var now = _timeProvider.GetUtcNow();
        var createdAtIso = FormatUtc(now);
        var wakeExpiresIso = FormatUtc(now.AddSeconds(SwarmBusConstants.DefaultWakeAttemptTtlSeconds));

        if (input.State is not null)
        {
            var dedup = TryDeduplicateLatestMessageInTx(connection, input, now, wakeExpiresIso);
            if (dedup is not null)
            {
                return dedup;
            }
        }

        if (input.Sender.Equals("watchdog", StringComparison.OrdinalIgnoreCase) && input.PrNumber.HasValue)
        {
            SupersedeWatchdogAlertsInTx(connection, input.WorkspaceId, input.PrNumber.Value, createdAtIso);
        }

        var id = InsertMessageInTx(connection, input, createdAtIso, wakeExpiresIso);
        return new SendTxResult(SendTxMode.Inserted, id, now);
    }

    private static SendTxResult? TryDeduplicateLatestMessageInTx(
        SqliteConnection connection,
        SendTxInput input,
        DateTimeOffset now,
        string wakeExpiresIso)
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
        latestCmd.Parameters.AddWithValue(ParamWorkspaceId, input.WorkspaceId);
        latestCmd.Parameters.AddWithValue("@sender", input.Sender);
        latestCmd.Parameters.AddWithValue("@recipient", input.Recipient);
        latestCmd.Parameters.AddWithValue("@topic", input.Topic);
        latestCmd.Parameters.AddWithValue(ParamPrNumber, (object?)input.PrNumber ?? DBNull.Value);
        latestCmd.Parameters.AddWithValue(ParamIssueNumber, (object?)input.IssueNumber ?? DBNull.Value);

        using var reader = latestCmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var latestState = GetNullableString(reader, 3);
        if (!string.Equals(latestState, input.State, StringComparison.Ordinal))
        {
            return null;
        }

        var matched = new MatchedMessageRow(
            Id: reader.GetInt64(0),
            Body: GetStringOrDefault(reader, 1, input.Body),
            Status: GetStringOrDefault(reader, 2, StatusUnread),
            WakeOk: GetNullableInt32(reader, 4),
            WakeState: GetNullableString(reader, 5),
            WakeAttemptExpiresAtUtc: GetNullableUtc(reader, 6));
        reader.Close();

        return ResolveMatchedStateRow(
            connection,
            matched,
            new WakeResolutionContext(input.Body, input.WakeTargetMatches, now, wakeExpiresIso));
    }

    private static void SupersedeWatchdogAlertsInTx(
        SqliteConnection connection,
        string workspaceId,
        int prNumber,
        string createdAtIso)
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
        supersedeCmd.Parameters.AddWithValue(ParamAckedAt, createdAtIso);
        supersedeCmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        supersedeCmd.Parameters.AddWithValue(ParamPrNumber, prNumber);
        supersedeCmd.ExecuteNonQuery();
    }

    private static long InsertMessageInTx(
        SqliteConnection connection,
        SendTxInput input,
        string createdAtIso,
        string wakeExpiresIso)
    {
        var isHeadless = SwarmBusConstants.HeadlessRecipients.Contains(input.Recipient);
        var initialStatus = isHeadless ? StatusAcked : StatusUnread;
        var initialAckedBy = isHeadless ? "auto-headless" : null;
        var initialAckedAt = isHeadless ? createdAtIso : null;
        int? initialWakeOk = input.WakeTargetMatches ? 0 : null;
        var initialWakeState = input.WakeTargetMatches ? WakeStatePending : null;
        var initialWakeExpires = input.WakeTargetMatches ? wakeExpiresIso : null;

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
        insertCmd.Parameters.AddWithValue(ParamWorkspaceId, input.WorkspaceId);
        insertCmd.Parameters.AddWithValue("@createdAt", createdAtIso);
        insertCmd.Parameters.AddWithValue("@sender", input.Sender);
        insertCmd.Parameters.AddWithValue("@recipient", input.Recipient);
        insertCmd.Parameters.AddWithValue("@topic", input.Topic);
        insertCmd.Parameters.AddWithValue("@body", input.Body);
        insertCmd.Parameters.AddWithValue(ParamPrNumber, (object?)input.PrNumber ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue(ParamIssueNumber, (object?)input.IssueNumber ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@priority", input.Priority);
        insertCmd.Parameters.AddWithValue("@replyToId", (object?)input.ReplyToId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue(ParamStatus, initialStatus);
        insertCmd.Parameters.AddWithValue("@ackedBy", (object?)initialAckedBy ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue(ParamAckedAt, (object?)initialAckedAt ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@state", (object?)input.State ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@wakeOk", (object?)initialWakeOk ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@wakeState", (object?)initialWakeState ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@wakeExpires", (object?)initialWakeExpires ?? DBNull.Value);

        return Convert.ToInt64(insertCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static SendTxResult ResolveMatchedStateRow(
        SqliteConnection connection,
        MatchedMessageRow latest,
        WakeResolutionContext ctx)
    {
        if (!ctx.WakeTargetMatches)
        {
            return new SendTxResult(SendTxMode.Deduplicated, latest.Id, ctx.Now, WakeState: latest.WakeState);
        }

        var curWakeState = NormalizeWakeState(latest.WakeState, latest.WakeOk);
        if (string.Equals(curWakeState, WakeStateOk, StringComparison.Ordinal))
        {
            return new SendTxResult(SendTxMode.Deduplicated, latest.Id, ctx.Now, WakeState: WakeStateOk);
        }

        var hasUnexpiredPending = string.Equals(curWakeState, WakeStatePending, StringComparison.Ordinal) &&
                                  latest.WakeAttemptExpiresAtUtc.HasValue &&
                                  latest.WakeAttemptExpiresAtUtc.Value > ctx.Now;
        if (hasUnexpiredPending)
        {
            return new SendTxResult(SendTxMode.Deduplicated, latest.Id, ctx.Now, WakeState: WakeStatePending, WakeInProgress: true);
        }

        if (string.Equals(latest.Status, StatusUnread, StringComparison.Ordinal))
        {
            ReserveWakeRetryInTx(connection, latest.Id, ctx.WakeExpiresIso);
            return new SendTxResult(
                SendTxMode.RetryWake,
                latest.Id,
                ctx.Now,
                Body: string.IsNullOrWhiteSpace(latest.Body) ? ctx.FallbackBody : latest.Body);
        }

        return new SendTxResult(SendTxMode.Deduplicated, latest.Id, ctx.Now, WakeState: curWakeState);
    }

    private static string? NormalizeWakeState(string? wakeState, int? wakeOk)
    {
        if (!string.IsNullOrEmpty(wakeState))
        {
            return wakeState;
        }

        return wakeOk switch
        {
            1 => WakeStateOk,
            0 => WakeStateFailed,
            _ => null
        };
    }

    private static void ReserveWakeRetryInTx(SqliteConnection connection, long messageId, string wakeExpiresIso)
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
        reserveCmd.Parameters.AddWithValue(ParamId, messageId);
        reserveCmd.ExecuteNonQuery();
    }

    private async Task<SwarmWakeResult> DispatchWakeAsync(SwarmWakeContext context, CancellationToken cancellationToken)
    {
        SwarmWakeResult wakeRes;
        try
        {
            wakeRes = await _wakeDispatcher!(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            wakeRes = new SwarmWakeResult(Attempted: true, Ok: false, Target: context.Recipient, ThreadId: context.CodexThreadId, Error: ex.Message);
        }

        CompleteWakeAttempt(context.MessageId, wakeRes.Ok);
        return wakeRes;
    }

    private static (bool AllWorkspaces, string NormalizedWorkspace) ResolveWorkspaceFilter(string? workspaceId)
    {
        var allWorkspaces = string.Equals(workspaceId?.Trim(), AllWildcard, StringComparison.Ordinal);
        var normalized = allWorkspaces ? AllWildcard : PathPatternMatcher.NormalizeWorkspaceId(workspaceId);
        return (allWorkspaces, normalized);
    }

    private static (int PrNumber, string HeadSha, string Role) ValidateReviewClaimTarget(ReviewClaimRequest request)
    {
        if (!request.PrNumber.HasValue || request.PrNumber.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "bus_review_claim requires positive integer \"pr_number\".");
        }

        var headSha = NormalizeHeadSha(request.HeadSha, required: true, nameof(request))!;
        var role = (request.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (!SwarmBusConstants.ValidReviewRoles.Contains(role))
        {
            throw new ArgumentException(
                $"bus_review_claim requires \"role\" to be one of: {string.Join(", ", SwarmBusConstants.ValidReviewRoles)}.",
                nameof(request));
        }

        return (request.PrNumber.Value, headSha, role);
    }

    private static string? NormalizeOptionalReviewRole(string? rawRole, string paramName)
    {
        if (string.IsNullOrWhiteSpace(rawRole))
        {
            return null;
        }

        var role = rawRole.Trim().ToLowerInvariant();
        if (!SwarmBusConstants.ValidReviewRoles.Contains(role))
        {
            throw new ArgumentException(
                $"bus_review_claim list \"role\" must be one of: {string.Join(", ", SwarmBusConstants.ValidReviewRoles)}.",
                paramName);
        }

        return role;
    }

    private static string? NormalizeHeadSha(string? rawSha, bool required, string paramName)
    {
        var trimmed = (rawSha ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                throw new ArgumentException("bus_review_claim requires a 40-character hexadecimal \"head_sha\".", paramName);
            }

            return null;
        }

        if (!HeadShaRegex().IsMatch(trimmed))
        {
            throw new ArgumentException("bus_review_claim \"head_sha\" must be a 40-character hexadecimal SHA.", paramName);
        }

        return trimmed.ToLowerInvariant();
    }

    private static string NormalizePriority(string? rawPriority)
    {
        var candidate = (rawPriority ?? NormalPriority).Trim().ToLowerInvariant();
        return SwarmBusConstants.ValidPriorities.Contains(candidate) ? candidate : NormalPriority;
    }

    private static string RequireNonEmpty(string? value, string errorMessage, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(errorMessage, paramName);
        }

        return value.Trim();
    }

    private static void PruneExpiredLocksInTx(SqliteConnection connection, DateTimeOffset now)
    {
        var expiredIds = new List<long>();
        using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = "SELECT id, expires_at_utc FROM locks";
            using var reader = selectCmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var expiresAt = GetNullableUtc(reader, 1) ?? DateTimeOffset.MinValue;
                if (expiresAt <= now)
                {
                    expiredIds.Add(id);
                }
            }
        }

        DeleteLocksByIdInTx(connection, expiredIds);
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
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
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
                PrNumber: GetNullableInt32(reader, 4),
                ClaimedAtUtc: ParseUtcOrMin(reader.GetString(5)),
                ExpiresAtUtc: ParseUtcOrMin(reader.GetString(6)),
                WorkspaceId: GetStringOrDefault(reader, 1, SwarmBusConstants.DefaultWorkspaceId)));
        }

        return locks;
    }

    private static List<SwarmMessageEnvelope> ReadMessages(SqliteCommand cmd)
    {
        var messages = new List<SwarmMessageEnvelope>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(MapMessageEnvelope(reader));
        }

        return messages;
    }

    private static SwarmMessageEnvelope MapMessageEnvelope(SqliteDataReader reader) =>
        new(
            Id: reader.GetInt64(0),
            CreatedAtUtc: ParseUtcOrMin(reader.GetString(2)),
            Sender: reader.GetString(3),
            Recipient: reader.GetString(4),
            Topic: reader.GetString(5),
            Body: reader.GetString(6),
            PrNumber: GetNullableInt32(reader, 7),
            IssueNumber: GetNullableInt32(reader, 8),
            Priority: GetStringOrDefault(reader, 9, NormalPriority),
            ReplyToId: GetNullableInt64(reader, 10),
            Status: GetStringOrDefault(reader, 11, StatusUnread),
            AckedBy: GetNullableString(reader, 12),
            AckedAtUtc: GetNullableUtc(reader, 13),
            State: GetNullableString(reader, 14),
            WakeOk: GetNullableBool(reader, 15),
            WakeState: GetNullableString(reader, 16),
            WakeAttemptExpiresAtUtc: GetNullableUtc(reader, 17),
            WorkspaceId: GetStringOrDefault(reader, 1, SwarmBusConstants.DefaultWorkspaceId));

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
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        cmd.Parameters.AddWithValue(ParamPrNumber, prNumber);
        cmd.Parameters.AddWithValue(ParamHeadSha, headSha);
        cmd.Parameters.AddWithValue(ParamRole, role);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapReviewClaim(reader) : null;
    }

    private static ReviewClaim MapReviewClaim(SqliteDataReader reader) =>
        new(
            Id: reader.GetInt64(0),
            PrNumber: reader.GetInt32(2),
            HeadSha: reader.GetString(3).ToLowerInvariant(),
            Role: reader.GetString(4),
            Agent: reader.GetString(5),
            ClaimedAtUtc: ParseUtcOrMin(reader.GetString(6)),
            Status: GetStringOrDefault(reader, 7, StatusClaimed),
            ExpiresAtUtc: GetNullableUtc(reader, 8),
            CompletedAtUtc: GetNullableUtc(reader, 9),
            WorkspaceId: GetStringOrDefault(reader, 1, SwarmBusConstants.DefaultWorkspaceId));

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
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        cmd.Parameters.AddWithValue(ParamIssueNumber, issueNumber);

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
        cmd.Parameters.AddWithValue(ParamId, id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapWorkQueueItem(reader) : null;
    }

    private static List<WorkQueueItem> ReadWorkQueueItems(SqliteConnection connection, string workspaceId, string? statusFilter)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, issue_number, title, priority, risk_class, status,
                   claimed_by, claimed_at_utc, lease_expires_utc, updated_at_utc
            FROM work_queue
            WHERE workspace_id = @workspaceId
              AND (@status IS NULL OR status = @status)
            ORDER BY priority ASC, id ASC
            """;
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        cmd.Parameters.AddWithValue(ParamStatus, (object?)statusFilter ?? DBNull.Value);

        var items = new List<WorkQueueItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapWorkQueueItem(reader));
        }

        return items;
    }

    private static WorkQueueItem MapWorkQueueItem(SqliteDataReader reader) =>
        new(
            Id: reader.GetInt64(0),
            IssueNumber: reader.GetInt32(2),
            Title: reader.GetString(3),
            Priority: reader.GetInt32(4),
            RiskClass: GetNullableString(reader, 5),
            Status: reader.GetString(6),
            ClaimedBy: GetNullableString(reader, 7),
            ClaimedAtUtc: GetNullableUtc(reader, 8),
            LeaseExpiresUtc: GetNullableUtc(reader, 9),
            UpdatedAtUtc: ParseUtcOrMin(reader.GetString(10)),
            WorkspaceId: GetStringOrDefault(reader, 1, SwarmBusConstants.DefaultWorkspaceId));

    private static Dictionary<string, string> ReadMetaMap(SqliteConnection connection, string workspaceId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT key, value
            FROM meta
            WHERE workspace_id = @workspaceId
            ORDER BY key ASC
            """;
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);

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
        cmd.Parameters.AddWithValue(ParamWorkspaceId, workspaceId);
        cmd.Parameters.AddWithValue(ParamKey, key);
        var raw = cmd.ExecuteScalar() as string;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static void MigrateAndCreateSchema(SqliteConnection connection)
    {
        EnsureMessagesTable(connection);
        EnsureLocksTable(connection);
        EnsureReviewClaimsTable(connection);
        EnsureWorkQueueTable(connection);
        EnsureMetaTable(connection);
    }

    private static void EnsureMessagesTable(SqliteConnection connection)
    {
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

        EnsureTableColumns(connection, TableMessages, [
            (ColWorkspaceId, "ALTER TABLE messages ADD COLUMN workspace_id TEXT NOT NULL DEFAULT 'default';"),
            ("state", "ALTER TABLE messages ADD COLUMN state TEXT;"),
            ("wake_ok", "ALTER TABLE messages ADD COLUMN wake_ok INTEGER;"),
            ("wake_state", "ALTER TABLE messages ADD COLUMN wake_state TEXT;"),
            ("wake_attempt_expires_at_utc", "ALTER TABLE messages ADD COLUMN wake_attempt_expires_at_utc TEXT;")
        ]);

        ExecuteNonQuery(connection, """
            CREATE INDEX IF NOT EXISTS idx_messages_recipient_status
              ON messages (recipient, status, id DESC);
            CREATE INDEX IF NOT EXISTS idx_messages_workspace_recipient_status
              ON messages (workspace_id, recipient, status, id DESC);
            """);
    }

    private static void EnsureLocksTable(SqliteConnection connection)
    {
        if (!TableExists(connection, TableLocks))
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
        else if (!GetTableColumns(connection, TableLocks).Contains(ColWorkspaceId))
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
    }

    private static void EnsureReviewClaimsTable(SqliteConnection connection)
    {
        if (!TableExists(connection, TableReviewClaims))
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
            return;
        }

        EnsureTableColumns(connection, TableReviewClaims, [
            (ColStatus, "ALTER TABLE review_claims ADD COLUMN status TEXT NOT NULL DEFAULT 'claimed';"),
            ("expires_at_utc", "ALTER TABLE review_claims ADD COLUMN expires_at_utc TEXT;"),
            ("completed_at_utc", "ALTER TABLE review_claims ADD COLUMN completed_at_utc TEXT;")
        ]);

        if (!GetTableColumns(connection, TableReviewClaims).Contains(ColWorkspaceId))
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

    private static void EnsureWorkQueueTable(SqliteConnection connection)
    {
        if (!TableExists(connection, TableWorkQueue))
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
        else if (!GetTableColumns(connection, TableWorkQueue).Contains(ColWorkspaceId))
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
    }

    private static void EnsureMetaTable(SqliteConnection connection)
    {
        if (!TableExists(connection, TableMeta))
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
        else if (!GetTableColumns(connection, TableMeta).Contains(ColWorkspaceId))
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
        var pragmaSql = tableName switch
        {
            TableMessages => "PRAGMA table_info(messages);",
            TableLocks => "PRAGMA table_info(locks);",
            TableReviewClaims => "PRAGMA table_info(review_claims);",
            TableWorkQueue => "PRAGMA table_info(work_queue);",
            TableMeta => "PRAGMA table_info(meta);",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), $"Unsupported SwarmBus table '{tableName}'.")
        };

        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = pragmaSql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            cols.Add(reader.GetString(1));
        }

        return cols;
    }

    private static void EnsureTableColumns(SqliteConnection connection, string tableName, (string Name, string AlterSql)[] columnDefs)
    {
        var existingCols = GetTableColumns(connection, tableName);
        foreach (var (colName, alterSql) in columnDefs)
        {
            if (!existingCols.Contains(colName))
            {
                ExecuteNonQuery(connection, alterSql);
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
        for (var attempt = 0; attempt <= _maxBusyRetries; attempt++)
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

        throw new InvalidOperationException("Busy retry loop terminated unexpectedly.");
    }

    private static bool IsBusyOrLocked(SqliteException ex) =>
        ex.SqliteErrorCode is SqliteBusyErrorCode or SqliteLockedErrorCode;

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string GetStringOrDefault(SqliteDataReader reader, int ordinal, string fallback) =>
        reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static long? GetNullableInt64(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static bool? GetNullableBool(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) != 0;

    private static DateTimeOffset? GetNullableUtc(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseUtcNullable(reader.GetString(ordinal));

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
