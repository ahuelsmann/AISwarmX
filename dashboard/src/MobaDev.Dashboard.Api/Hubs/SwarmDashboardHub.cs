using Microsoft.AspNetCore.SignalR;
using MobaDev.Dashboard.Api.Contracts;

namespace MobaDev.Dashboard.Api.Hubs;

/// <summary>
/// Strictly read-only SignalR client contract for live Swarm Bus and Dashboard updates.
/// </summary>
public interface ISwarmDashboardClient
{
    Task SwarmBusUpdated(SwarmBusResponse swarmBus);

    Task SnapshotUpdated(DashboardSnapshot snapshot);
}

/// <summary>
/// Read-only real-time push hub mapped at <c>/hubs/swarm</c>.
/// </summary>
public sealed class SwarmDashboardHub : Hub<ISwarmDashboardClient>;
