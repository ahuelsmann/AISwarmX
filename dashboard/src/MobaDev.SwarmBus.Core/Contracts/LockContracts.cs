namespace MobaDev.SwarmBus.Core.Contracts;

/// <summary>
/// Active path-prefix or glob lock lease owned by an agent within a workspace.
/// </summary>
public sealed record PathLockLease(
    long Id,
    string Agent,
    string PathPattern,
    int? PrNumber,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string WorkspaceId = SwarmBusConstants.DefaultWorkspaceId);

/// <summary>
/// Conflict detail when a lock claim overlaps an active lease held by another agent in the same workspace.
/// </summary>
public sealed record LockConflict(
    string RequestedPath,
    string ConflictingAgent,
    string ConflictingPath,
    int? ConflictingPr,
    DateTimeOffset ExpiresAtUtc,
    string WorkspaceId = SwarmBusConstants.DefaultWorkspaceId);

/// <summary>
/// Request to claim one or more repo-relative path patterns for an agent.
/// </summary>
public sealed record LockClaimRequest(
    string Agent,
    IReadOnlyList<string> Paths,
    int? PrNumber = null,
    int TtlMinutes = SwarmBusConstants.DefaultLockTtlMinutes,
    string? WorkspaceId = null);

/// <summary>
/// Result of a lock claim operation.
/// </summary>
public sealed record LockClaimResult(
    bool Ok,
    string WorkspaceId,
    IReadOnlyList<string> ClaimedPaths,
    IReadOnlyList<LockConflict> Conflicts,
    IReadOnlyList<PathLockLease> Locks);

/// <summary>
/// Request to release locks by explicit paths, agent, and/or PR number within a workspace.
/// </summary>
public sealed record LockReleaseRequest(
    string? Agent = null,
    IReadOnlyList<string>? Paths = null,
    int? PrNumber = null,
    string? WorkspaceId = null);

/// <summary>
/// Result of a lock release operation.
/// </summary>
public sealed record LockReleaseResult(
    bool Ok,
    string WorkspaceId,
    int RemovedCount,
    IReadOnlyList<PathLockLease> RemainingLocks);
