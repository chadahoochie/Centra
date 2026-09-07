namespace Centra.Sample.Actors.Simulation;

public sealed record ActorSimulationResult(
    string ActorId,
    decimal FinalBalance,
    int ConcurrentDepositsCount,
    bool PassivationAndReactivationSucceeded,
    bool ReminderExecuted,
    TimeSpan ElapsedDuration,
    IReadOnlyList<string> LogEntries);
