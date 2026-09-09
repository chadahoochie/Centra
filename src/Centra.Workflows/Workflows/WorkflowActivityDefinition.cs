namespace Centra.Core.Workflows;

/// <summary>
/// Encapsulates metadata for a registered activity definition.
/// </summary>
public sealed record WorkflowActivityDefinition(
    string Name,
    Type ActivityType,
    Type InputType,
    Type OutputType);
