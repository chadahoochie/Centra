namespace Centra.Sample.TenantOffload.Simulation;

public sealed record TenantOffloadSimulationResult(
    bool BaselineNormalSuccess,
    bool NoisyNeighborDetectedAndOffloaded,
    bool FairSchedulingIsolationSuccess,
    bool BrokerTopicShardingSuccess,
    bool CooldownAndRecoverySuccess,
    long TotalElapsedMs,
    IReadOnlyList<string> SummaryNotes,
    bool MultiInstanceConsumptionSuccess = true)
{
    public bool AllStepsSucceeded =>
        BaselineNormalSuccess &&
        NoisyNeighborDetectedAndOffloaded &&
        FairSchedulingIsolationSuccess &&
        BrokerTopicShardingSuccess &&
        CooldownAndRecoverySuccess &&
        MultiInstanceConsumptionSuccess;
}
