namespace Centra.ControlPlane.Workflows;

public sealed record WorkflowActivityDto(
    string Name,
    string ActivityType,
    string InputType,
    string OutputType);
