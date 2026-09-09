namespace Centra.Core.Workflows;

/// <summary>
/// Encapsulates metadata for a registered workflow definition.
/// </summary>
public sealed record WorkflowDefinition(
    string Name,
    Type WorkflowType,
    Type InputType,
    Type OutputType);
