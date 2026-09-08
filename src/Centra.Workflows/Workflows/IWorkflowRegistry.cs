namespace Centra.Core.Workflows;

/// <summary>
/// Registry for discovering registered workflow and activity definitions.
/// </summary>
public interface IWorkflowRegistry
{
    void RegisterWorkflow(WorkflowDefinition definition);
    void RegisterActivity(WorkflowActivityDefinition definition);

    bool TryGetWorkflow(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorkflowDefinition? definition);
    bool TryGetActivity(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorkflowActivityDefinition? definition);

    IReadOnlyCollection<WorkflowDefinition> GetWorkflows();
    IReadOnlyCollection<WorkflowActivityDefinition> GetActivities();
}
