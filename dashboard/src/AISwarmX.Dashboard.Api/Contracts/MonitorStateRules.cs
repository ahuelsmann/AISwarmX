using AISwarmX.Dashboard.Api.Configuration;

namespace AISwarmX.Dashboard.Api.Contracts;

/// <summary>
/// Shared monitor state transition rules applied across monitor providers.
/// </summary>
public static class MonitorStateRules
{
    /// <summary>
    /// Downgrades a <c>busy</c> state to <c>idle</c> when no recent activity timestamp exists
    /// or elapsed inactivity exceeds <paramref name="threshold"/>.
    /// </summary>
    public static string ApplyIdle(
        string state,
        DateTimeOffset? lastActivityUtc,
        DateTimeOffset now,
        TimeSpan threshold)
    {
        if (string.Equals(state, "busy", StringComparison.OrdinalIgnoreCase) &&
            (!lastActivityUtc.HasValue || (now - lastActivityUtc.Value) > threshold))
        {
            return "idle";
        }

        return state;
    }

    /// <summary>
    /// Resolves monitor state and effective rate-limit label from raw busy status, optional active
    /// quota-pause label, and activity freshness. Live busy stays <c>busy</c>; stale busy preserves
    /// an active quota pause as <c>rate_limited</c> rather than downgrading to <c>idle</c>.
    /// </summary>
    public static (string State, string? RateLimitResetLabel) ResolveState(
        bool isBusy,
        string? rateLimitResetLabel,
        DateTimeOffset? lastActivityUtc,
        DateTimeOffset now,
        TimeSpan threshold)
    {
        if (isBusy)
        {
            var resolved = ApplyIdle("busy", lastActivityUtc, now, threshold);
            if (string.Equals(resolved, "busy", StringComparison.Ordinal))
            {
                return ("busy", null);
            }

            return !string.IsNullOrEmpty(rateLimitResetLabel)
                ? ("rate_limited", rateLimitResetLabel)
                : ("idle", null);
        }

        return !string.IsNullOrEmpty(rateLimitResetLabel)
            ? ("rate_limited", rateLimitResetLabel)
            : ("standby", null);
    }

    /// <summary>
    /// Ensures a positive busy-idle threshold, falling back to <see cref="DashboardOptions.DefaultBusyIdleAfterMinutes"/>.
    /// </summary>
    public static TimeSpan NormalizeThreshold(TimeSpan threshold)
    {
        return threshold > TimeSpan.Zero
            ? threshold
            : TimeSpan.FromMinutes(DashboardOptions.DefaultBusyIdleAfterMinutes);
    }
}
