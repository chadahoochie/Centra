using Centra.Events;

namespace Centra.Sample.MultiInstance.Domain;

[EventContract("centra.cluster.task.dispatched")]
public sealed record ClusterTaskEvent(
    string TaskId,
    string TaskType,
    string AssignedByInstanceId,
    string Payload,
    DateTimeOffset CreatedAtUtc);
