namespace Centra.State;

public sealed record StateOptions
{
    public TimeSpan? TimeToLive { get; init; }
    public ConcurrencyMode Concurrency { get; init; } = ConcurrencyMode.FirstWriteWins;
    public bool DisableResilience { get; init; } = false;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
