using Centra.Core.Workflows;

namespace Centra.Hosting.Routing;

/// <summary>
/// Registration entry for a configured workflow activity definition.
/// </summary>
public sealed record CentraWorkflowActivityRegistration(WorkflowActivityDefinition Definition);
