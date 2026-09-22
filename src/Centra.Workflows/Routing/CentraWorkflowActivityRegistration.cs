using Centra.Core.Workflows;

namespace Centra.Workflows.Routing;

/// <summary>
/// Registration entry for a configured workflow activity definition.
/// </summary>
public sealed record CentraWorkflowActivityRegistration(WorkflowActivityDefinition Definition);
