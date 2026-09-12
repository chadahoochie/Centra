namespace Centra.Core.Workflows;

/// <summary>
/// Serializable DTO for persisting durable workflow timers in a state store.
/// </summary>
public sealed record DurableWorkflowTimerDto(
    string InstanceId,
    int EventId,
    DateTimeOffset DueTimeUtc,
    DateTimeOffset CreatedAtUtc);
