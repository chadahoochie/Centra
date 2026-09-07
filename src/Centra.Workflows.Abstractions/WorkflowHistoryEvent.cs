namespace Centra.Workflows;

/// <summary>
/// Immutable record of an event in a workflow execution history stream.
/// </summary>
public readonly record struct WorkflowHistoryEvent(
    long EventId,
    WorkflowHistoryEventType EventType,
    string Name,
    DateTimeOffset Timestamp,
    ReadOnlyMemory<byte> Data,
    string? Details);
