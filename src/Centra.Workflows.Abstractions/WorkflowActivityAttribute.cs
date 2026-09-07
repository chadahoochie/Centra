namespace Centra.Workflows;

/// <summary>
/// Specifies the activity registration name and configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class WorkflowActivityAttribute : Attribute
{
    public string Name { get; }

    public WorkflowActivityAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
