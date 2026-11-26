using MobaDev.Dashboard.Api.Configuration;

namespace MobaDev.Dashboard.Api.Contracts;

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
    /// Ensures a positive busy-idle threshold, falling back to <see cref="DashboardOptions.DefaultBusyIdleAfterMinutes"/>.
    /// </summary>
    public static TimeSpan NormalizeThreshold(TimeSpan threshold)
    {
        return threshold > TimeSpan.Zero
            ? threshold
            : TimeSpan.FromMinutes(DashboardOptions.DefaultBusyIdleAfterMinutes);
    }
}
