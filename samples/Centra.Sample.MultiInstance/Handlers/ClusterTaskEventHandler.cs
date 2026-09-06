using Centra.Events;
using Centra.PubSub;
using Centra.Sample.MultiInstance.Domain;
using Centra.Sample.MultiInstance.Services;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.MultiInstance.Handlers;

[Topic("cluster-pubsub", "cluster.tasks")]
public sealed class ClusterTaskEventHandler : IEventHandler<ClusterTaskEvent>
{
    private readonly IClusterNodeLocalState _localState;
    private readonly ILogger<ClusterTaskEventHandler> _logger;

    public ClusterTaskEventHandler(
        IClusterNodeLocalState localState,
        ILogger<ClusterTaskEventHandler> logger)
    {
        _localState = localState ?? throw new ArgumentNullException(nameof(localState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<EventHandlingResult> HandleAsync(
        ClusterTaskEvent @event,
        EventContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        _logger.LogInformation(
            "Node {InstanceId} received CloudEvent {TaskId} ({TaskType}) from {SenderInstanceId}. CorrelationId: {CorrelationId}",
            _localState.InstanceId,
            @event.TaskId,
            @event.TaskType,
            @event.AssignedByInstanceId,
            context.CorrelationId ?? CentraAmbientContext.CorrelationId ?? "none");

        _localState.RecordTask(new TaskExecutionRecord(
            @event.TaskId,
            @event.TaskType,
            _localState.InstanceId,
            DateTimeOffset.UtcNow));

        return Task.FromResult(EventHandlingResult.Success);
    }
}
