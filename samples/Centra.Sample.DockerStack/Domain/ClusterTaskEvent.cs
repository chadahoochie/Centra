using Centra.Events;

namespace Centra.Sample.DockerStack.Domain;

[EventContract("centra.dockerstack.task.dispatched")]
public sealed record ClusterTaskEvent(
    string TaskId,
    string TaskType,
    string AssignedByInstanceId,
    string Payload,
    DateTimeOffset CreatedAtUtc);
