namespace MobaDev.SwarmBus.Core.Contracts;

/// <summary>
/// Typed Swarm Bus message persisted in SQLite.
/// </summary>
public sealed record SwarmMessageEnvelope(
    long Id,
    DateTimeOffset CreatedAtUtc,
    string Sender,
    string Recipient,
    string Topic,
    string Body,
    int? PrNumber = null,
    int? IssueNumber = null,
    string Priority = "normal",
    long? ReplyToId = null,
    string Status = "unread",
    string? AckedBy = null,
    DateTimeOffset? AckedAtUtc = null,
    string? State = null,
    bool? WakeOk = null,
    string? WakeState = null,
    DateTimeOffset? WakeAttemptExpiresAtUtc = null,
    string WorkspaceId = SwarmBusConstants.DefaultWorkspaceId);

/// <summary>
/// Request payload to send a cross-tool Swarm Bus message.
/// </summary>
public sealed record SwarmSendRequest(
    string From,
    string To,
    string Topic,
    string Body,
    int? PrNumber = null,
    int? IssueNumber = null,
    string? Priority = null,
    long? ReplyToId = null,
    string? State = null,
    bool Wake = false,
    string? CodexThreadId = null,
    string? WorkspaceId = null);

/// <summary>
/// Outcome of attempting to dispatch a wake signal for a Swarm Bus message.
/// </summary>
public sealed record SwarmWakeResult(
    bool Attempted,
    bool Ok,
    string Target,
    string? ThreadId = null,
    string? Reason = null,
    string? Error = null);

/// <summary>
/// Context passed to an optional wake dispatcher callback.
/// </summary>
public sealed record SwarmWakeContext(
    long MessageId,
    string WorkspaceId,
    string Sender,
    string Recipient,
    int? PrNumber,
    string Topic,
    string Body,
    string? CodexThreadId,
    string FormattedMessage);

/// <summary>
/// Result returned by <c>SendMessage</c> / <c>SendMessageAsync</c>, including deduplication and wake metadata.
/// </summary>
public sealed record SwarmSendResult(
    bool Ok,
    long Id,
    string WorkspaceId,
    DateTimeOffset? CreatedAtUtc = null,
    string? From = null,
    string? To = null,
    string? Topic = null,
    int? PrNumber = null,
    int? IssueNumber = null,
    string? Priority = null,
    string? State = null,
    bool Deduplicated = false,
    bool WakeRetried = false,
    bool WakeInProgress = false,
    string? WakeState = null,
    IReadOnlyList<SwarmWakeResult>? WakeResults = null);

/// <summary>
/// Result returned by <c>GetInbox</c>.
/// </summary>
public sealed record SwarmInboxResult(
    string Agent,
    string WorkspaceId,
    bool UnreadOnly,
    int Count,
    IReadOnlyList<SwarmMessageEnvelope> Messages);

/// <summary>
/// Result returned by <c>AckMessage</c>.
/// </summary>
public sealed record SwarmAckResult(
    bool Ok,
    long AckedMessageId,
    string AckedBy,
    DateTimeOffset AckedAtUtc,
    SwarmSendResult? ReplyMessage = null);
