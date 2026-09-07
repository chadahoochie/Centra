using Centra.Core.Workflows;

namespace Centra.Hosting.Routing;

/// <summary>
/// Registration entry for a configured workflow definition.
/// </summary>
public sealed record CentraWorkflowRegistration(WorkflowDefinition Definition);
