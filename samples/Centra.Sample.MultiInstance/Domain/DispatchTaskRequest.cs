namespace Centra.Sample.MultiInstance.Domain;

public sealed record DispatchTaskRequest(
    string TaskType,
    string Payload);
