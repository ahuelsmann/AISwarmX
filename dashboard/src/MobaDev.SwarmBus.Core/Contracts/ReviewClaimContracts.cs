namespace MobaDev.SwarmBus.Core.Contracts;

/// <summary>
/// Single-holder review lease for a <c>(workspace_id, pr_number, head_sha, role)</c> tuple.
/// </summary>
public sealed record ReviewClaim(
    long Id,
    int PrNumber,
    string HeadSha,
    string Role,
    string Agent,
    DateTimeOffset ClaimedAtUtc,
    string Status,
    DateTimeOffset? ExpiresAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string WorkspaceId = SwarmBusConstants.DefaultWorkspaceId);

/// <summary>
/// Request payload for review claim lifecycle actions (<c>claim</c>, <c>complete</c>, <c>release</c>, <c>list</c>).
/// </summary>
public sealed record ReviewClaimRequest(
    string Action = "claim",
    string? Agent = null,
    int? PrNumber = null,
    string? HeadSha = null,
    string? Role = null,
    int? TtlMinutes = null,
    string? WorkspaceId = null);

/// <summary>
/// Result of a <c>ReviewClaim</c> operation.
/// </summary>
public sealed record ReviewClaimResult(
    bool Ok,
    string Action,
    string WorkspaceId,
    bool? Claimed = null,
    bool? Reclaimed = null,
    bool? Released = null,
    string? Reason = null,
    bool? AlreadyClaimedBySelf = null,
    string? Holder = null,
    string? PreviousHolder = null,
    string? PreviousStatus = null,
    int? PrNumber = null,
    string? HeadSha = null,
    string? Role = null,
    string? Status = null,
    ReviewClaim? Claim = null,
    IReadOnlyList<ReviewClaim>? Claims = null);
