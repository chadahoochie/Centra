namespace Centra.Sample.MultiInstance.Domain;

public sealed record TaskExecutionRecord(
    string TaskId,
    string TaskType,
    string HandledByInstanceId,
    DateTimeOffset CompletedAtUtc);
