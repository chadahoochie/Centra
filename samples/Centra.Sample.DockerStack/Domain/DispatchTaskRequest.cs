namespace Centra.Sample.DockerStack.Domain;

public sealed record DispatchTaskRequest(
    string TaskType,
    string Payload);
