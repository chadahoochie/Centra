namespace Centra.Sample.DockerStack.Domain;

public sealed record TaskExecutionRecord(
    string TaskId,
    string TaskType,
    string HandledByInstanceId,
    DateTimeOffset CompletedAtUtc);
