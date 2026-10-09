using System.Collections.Generic;

namespace Centra.Sample.ControlPlane.Simulation;

public sealed record ControlPlaneSimulationResult(
    bool HaRoutingSuccess,
    bool RogueNodeDefenseSuccess,
    bool MultiClusterExpansionSuccess,
    bool DynamicSyncSuccess,
    bool HaFailoverSuccess,
    bool DashboardVerificationSuccess,
    long TotalElapsedMs,
    IReadOnlyList<string> SummaryNotes)
{
    public bool AllStepsSucceeded =>
        HaRoutingSuccess &&
        RogueNodeDefenseSuccess &&
        MultiClusterExpansionSuccess &&
        DynamicSyncSuccess &&
        HaFailoverSuccess &&
        DashboardVerificationSuccess;
}
