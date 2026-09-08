using Centra.Bindings;
using Centra.Sample.DockerStack.Domain;
using Centra.Sample.DockerStack.Services;
using Centra.State;

namespace Centra.Sample.DockerStack.Cron;

/// <summary>
/// Registered identically on every replica. Centra's DistributedJobHandler wraps this with a
/// shared Redis lock keyed on the scheduled tick, so only one replica's ExecuteAsync call wins per tick.
/// </summary>
public sealed class ClusterHeartbeatJob : IJobHandler
{
    public const string JobName = "cluster-heartbeat-job";
    public const string LastRunStateKey = "cluster-heartbeat-job:last-run";

    private readonly IStateStore<CronLastRunState> _stateStore;
    private readonly IClusterNodeLocalState _localState;

    public ClusterHeartbeatJob(
        IStateStore<CronLastRunState> stateStore,
        IClusterNodeLocalState localState)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _localState = localState ?? throw new ArgumentNullException(nameof(localState));
    }

    public async ValueTask ExecuteAsync(ScheduledJobContext context)
    {
        var state = new CronLastRunState(
            context.JobName,
            _localState.InstanceId,
            context.Iteration,
            context.ActualTime);

        await _stateStore.SetAsync(LastRunStateKey, state, cancellationToken: context.CancellationToken)
            .ConfigureAwait(false);
    }
}
