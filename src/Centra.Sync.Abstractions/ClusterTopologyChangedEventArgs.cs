namespace Centra.Sync;

public sealed class ClusterTopologyChangedEventArgs : EventArgs
{
    public required IReadOnlyCollection<ServiceNodeDto> AddedNodes { get; init; }
    public required IReadOnlyCollection<ServiceNodeDto> RemovedNodes { get; init; }
    public required IReadOnlyCollection<ServiceNodeDto> CurrentSnapshot { get; init; }
}
