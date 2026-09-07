namespace Centra.Core.Workflows;

/// <summary>
/// Serializable record for persisting an individual workflow history event.
/// </summary>
public sealed class WorkflowHistoryEventRecord
{
    public long EventId { get; set; }
    public int EventType { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public byte[]? Data { get; set; }
    public string? Details { get; set; }
}
