namespace Centra.Sample.MultiInstance.Domain;

public sealed record SimulationResult(
    string ClusterName,
    int NodeCount,
    bool LeaderElectionSuccessful,
    string? ElectedLeaderInstanceId,
    bool ConcurrencyConflictResolved,
    long FinalCounterValue,
    int TotalTasksProcessed,
    TimeSpan ElapsedDuration,
    IReadOnlyList<string> LogEntries);
