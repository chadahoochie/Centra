namespace Centra.ControlPlane.HA;

public interface IControlPlaneLeaderTracker
{
    bool IsLeader { get; }
    string? LeaderEndpoint { get; }
    void SetLeader(bool isLeader, string? leaderEndpoint);
}
