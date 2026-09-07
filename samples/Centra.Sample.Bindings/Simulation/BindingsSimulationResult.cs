namespace Centra.Sample.Bindings.Simulation;

public sealed record BindingsSimulationResult(
    bool CronJobTriggered,
    long CronIterationsCompleted,
    bool OutputBindingDispatched,
    string OutputStatusCode,
    bool InputBindingTriggerHandled,
    string InputResponseText,
    bool DistributedCoordinationHonored,
    TimeSpan ElapsedDuration);
