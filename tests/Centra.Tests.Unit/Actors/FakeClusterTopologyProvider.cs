using Centra.Sync;

namespace Centra.Tests.Unit.Actors;

public sealed class FakeClusterTopologyProvider : IClusterTopologyProvider
{
    private readonly IReadOnlyCollection<ServiceNodeDto> _snapshot;

    public event EventHandler<ClusterTopologyChangedEventArgs>? TopologyChanged
    {
        add { }
        remove { }
    }

    public FakeClusterTopologyProvider(IReadOnlyCollection<ServiceNodeDto> snapshot)
    {
        _snapshot = snapshot;
    }

    public IReadOnlyCollection<ServiceNodeDto> GetSnapshot() => _snapshot;
}
