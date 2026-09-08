namespace Centra.Sample.DockerStack.Domain;

public sealed record CronLastRunState(
    string JobName,
    string HandledByInstanceId,
    long Iteration,
    DateTimeOffset ExecutedAtUtc);
