namespace Centra.ControlPlane.Workflows;

public sealed record WorkflowDefinitionDto(
    string Name,
    string WorkflowType,
    string InputType,
    string OutputType);
