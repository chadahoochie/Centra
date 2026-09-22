using Centra.Core.Workflows;

namespace Centra.Workflows.Routing;

/// <summary>
/// Registration entry for a configured workflow definition.
/// </summary>
public sealed record CentraWorkflowRegistration(WorkflowDefinition Definition);
