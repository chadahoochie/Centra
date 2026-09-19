namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// A single drain deadline shared by every subscription torn down during one shutdown, so the total
/// drain cost is the configured allowance rather than that allowance times the subscription count.
/// </summary>
internal sealed class ShutdownDrainWindow : IDisposable
{
    private readonly long _deadline;
    private int _closed;

    public ShutdownDrainWindow(TimeSpan budget)
        => _deadline = Environment.TickCount64 + (long)Math.Max(0d, budget.TotalMilliseconds);

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public TimeSpan Remaining => TimeSpan.FromMilliseconds(Math.Max(0L, _deadline - Environment.TickCount64));

    public void Dispose() => Volatile.Write(ref _closed, 1);
}
