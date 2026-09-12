namespace Centra.Sample.Bindings.Simulation;

/// <summary>
/// Summary results of the multi-instance bindings and cron simulation.
/// </summary>
public sealed record BindingsSimulationResult(
    int TotalNodesRunning,
    int CronTicksObserved,
    int ClusterTotalExecutions,
    int RedundantExecutionsPrevented,
    bool DistributedCoordinationHonored,
    bool OutputBindingDispatched,
    string OutputStatusCode,
    bool InputBindingTriggerHandled,
    string InputResponseText,
    TimeSpan ElapsedDuration)
{
    /// <summary>
    /// Backward-compatible indicator that at least one cron iteration executed.
    /// </summary>
    public bool CronJobTriggered => ClusterTotalExecutions > 0;

    /// <summary>
    /// Backward-compatible count of total cron iterations completed across the cluster.
    /// </summary>
    public long CronIterationsCompleted => ClusterTotalExecutions;
}
