namespace Centra.Invocation;

public sealed record ServiceInvocationOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; init; } = 3;
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}
