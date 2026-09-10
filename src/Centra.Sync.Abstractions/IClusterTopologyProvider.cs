namespace Centra.Sync;

/// <summary>
/// Tracks live cluster membership by polling the Control Plane's topology and provides both a
/// current snapshot and change notifications. This is the single source of truth for "what nodes
/// exist right now" - consumers key the same feed differently depending on their needs:
/// actor placement (IActorPlacementDirector) partitions by
/// <see cref="ServiceNodeDto.InstanceId"/> because it must pick exactly one physical replica to
/// own an actor, while service invocation routing partitions by <see cref="ServiceNodeDto.AppId"/>
/// because it deliberately load-balances across any healthy replica of a logical service. Both
/// keys are legitimate over the same underlying data - this is not a redundancy to unify further.
/// </summary>
public interface IClusterTopologyProvider
{
    IReadOnlyCollection<ServiceNodeDto> GetSnapshot();

    event EventHandler<ClusterTopologyChangedEventArgs>? TopologyChanged;
}

public sealed class ClusterTopologyChangedEventArgs : EventArgs
{
    public required IReadOnlyCollection<ServiceNodeDto> AddedNodes { get; init; }
    public required IReadOnlyCollection<ServiceNodeDto> RemovedNodes { get; init; }
    public required IReadOnlyCollection<ServiceNodeDto> CurrentSnapshot { get; init; }
}
