namespace MobaDev.SwarmBus.Core.Contracts;

/// <summary>
/// Shared constants for MOBAi Swarm Bus contracts and persistence.
/// </summary>
public static class SwarmBusConstants
{
    public const string DefaultWorkspaceId = "default";
    public const int DefaultWakeAttemptTtlSeconds = 120;
    public const int DefaultLockTtlMinutes = 120;
    public const int MinLockTtlMinutes = 5;
    public const int MaxLockTtlMinutes = 1440;
    public const int DefaultReviewClaimTtlMinutes = 60;
    public const int MinReviewClaimTtlMinutes = 1;
    public const int MaxReviewClaimTtlMinutes = 1440;
    public const int DefaultQueueLeaseHours = 4;
    public const int CodeWipClaimLimit = 2;

    public static readonly IReadOnlySet<string> HeadlessRecipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "watchdog",
        "claude-opus-worker",
        "system"
    };

    public static readonly IReadOnlySet<string> ValidPriorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "low",
        "normal",
        "high",
        "urgent"
    };

    public static readonly IReadOnlySet<string> ValidReviewRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "companion",
        "final",
        "p3-verification"
    };

    public static readonly IReadOnlySet<string> ValidQueueStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "queued",
        "claimed",
        "done",
        "removed"
    };
}
