namespace Centra.Workflows;

/// <summary>
/// Strongly typed identifier for a workflow instance.
/// </summary>
public readonly record struct WorkflowInstanceId : IEquatable<WorkflowInstanceId>, IComparable<WorkflowInstanceId>
{
    public string Value { get; }

    public WorkflowInstanceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public static WorkflowInstanceId New() => new($"wf-{Guid.NewGuid():N}");

    public static implicit operator string(WorkflowInstanceId id) => id.Value;
    public static implicit operator WorkflowInstanceId(string value) => new(value);

    public int CompareTo(WorkflowInstanceId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    public override string ToString() => Value ?? string.Empty;
}
