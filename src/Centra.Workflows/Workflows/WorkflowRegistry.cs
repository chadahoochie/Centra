using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Centra.Core.Workflows;

/// <summary>
/// Thread-safe registry for workflow and activity definitions.
/// </summary>
public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _workflows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WorkflowActivityDefinition> _activities = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterWorkflow(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _workflows[definition.Name] = definition;
    }

    public void RegisterActivity(WorkflowActivityDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _activities[definition.Name] = definition;
    }

    public bool TryGetWorkflow(string name, [NotNullWhen(true)] out WorkflowDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _workflows.TryGetValue(name, out definition);
    }

    public bool TryGetActivity(string name, [NotNullWhen(true)] out WorkflowActivityDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _activities.TryGetValue(name, out definition);
    }

    public IReadOnlyCollection<WorkflowDefinition> GetWorkflows() => _workflows.Values.ToArray();

    public IReadOnlyCollection<WorkflowActivityDefinition> GetActivities() => _activities.Values.ToArray();
}
