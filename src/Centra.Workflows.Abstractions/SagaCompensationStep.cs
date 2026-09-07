namespace Centra.Workflows;

/// <summary>
/// Represents a registered compensation step in a saga.
/// </summary>
public readonly record struct SagaCompensationStep(string ActivityName, object? Input);
