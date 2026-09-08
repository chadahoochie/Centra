namespace Centra.Sample.DockerStack.Domain;

public sealed record IncrementCounterRequest(
    long IncrementBy = 1,
    bool SimulateConflict = false);
