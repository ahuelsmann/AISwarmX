namespace MobaDev.SwarmBus.Core.Contracts;

/// <summary>
/// Persistent Ready work queue item scoped to a workspace.
/// </summary>
public sealed record WorkQueueItem(
    long Id,
    int IssueNumber,
    string Title,
    int Priority,
    string? RiskClass,
    string Status,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? LeaseExpiresUtc,
    DateTimeOffset UpdatedAtUtc,
    string WorkspaceId = SwarmBusConstants.DefaultWorkspaceId);

/// <summary>
/// Request payload to insert or update a work queue item.
/// </summary>
public sealed record WorkQueueUpsertRequest(
    int IssueNumber,
    string? Title = null,
    int? Priority = null,
    string? RiskClass = null,
    string Status = "queued",
    string? WorkspaceId = null);

/// <summary>
/// Result returned by <c>QueueNext</c> including resume status and effective code WIP metrics.
/// </summary>
public sealed record WorkQueueNextResult(
    bool Ok,
    string WorkspaceId,
    bool Resumed,
    int CodeWip,
    int ActiveClaimedCodeCount,
    int EffectiveCodeWip,
    WorkQueueItem? Item,
    string? Reason = null);

/// <summary>
/// Result returned by <c>QueueUpsert</c>, <c>QueueRelease</c>, or <c>QueueDone</c>.
/// </summary>
public sealed record WorkQueueMutationResult(
    bool Ok,
    string Action,
    string WorkspaceId,
    int IssueNumber,
    bool Changed,
    WorkQueueItem? Item);

/// <summary>
/// Result returned by <c>QueueList</c>.
/// </summary>
public sealed record WorkQueueListResult(
    bool Ok,
    string WorkspaceId,
    int CodeWip,
    IReadOnlyDictionary<string, string> Meta,
    IReadOnlyList<WorkQueueItem> Items);

/// <summary>
/// Unread count summary for a single recipient.
/// </summary>
public sealed record RecipientUnreadCount(
    string Recipient,
    int UnreadCount);

/// <summary>
/// Aggregated Swarm Bus status snapshot.
/// </summary>
public sealed record SwarmBusStatusSnapshot(
    DateTimeOffset GeneratedAtUtc,
    string WorkspaceId,
    int TotalUnread,
    IReadOnlyList<RecipientUnreadCount> UnreadByRecipient,
    IReadOnlyList<PathLockLease> ActiveLocks,
    IReadOnlyList<SwarmMessageEnvelope> RecentMessages);
