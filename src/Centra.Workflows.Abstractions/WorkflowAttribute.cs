namespace Centra.Workflows;

/// <summary>
/// Specifies the workflow registration name and configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class WorkflowAttribute : Attribute
{
    public string Name { get; }

    public WorkflowAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
