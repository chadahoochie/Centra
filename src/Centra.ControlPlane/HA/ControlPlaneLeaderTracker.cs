namespace Centra.ControlPlane.HA;

public sealed class ControlPlaneLeaderTracker : IControlPlaneLeaderTracker
{
    private volatile bool _isLeader;
    private volatile string? _leaderEndpoint;

    public bool IsLeader => _isLeader;
    public string? LeaderEndpoint => _leaderEndpoint;

    public void SetLeader(bool isLeader, string? leaderEndpoint)
    {
        _isLeader = isLeader;
        _leaderEndpoint = leaderEndpoint;
    }
}
