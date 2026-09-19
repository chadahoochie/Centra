namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// A single drain deadline shared by every subscription torn down during one shutdown, so the total
/// drain cost is the configured allowance rather than that allowance times the subscription count.
/// An allowance of <see cref="Timeout.InfiniteTimeSpan"/> means the window never expires.
/// </summary>
internal sealed class ShutdownDrainWindow : IDisposable
{
    private readonly bool _unbounded;
    private readonly long _deadline;
    private int _closed;

    public ShutdownDrainWindow(TimeSpan budget)
    {
        _unbounded = budget == Timeout.InfiniteTimeSpan;
        _deadline = _unbounded ? 0L : Environment.TickCount64 + (long)budget.TotalMilliseconds;
    }

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public TimeSpan Remaining => _unbounded
        ? Timeout.InfiniteTimeSpan
        : TimeSpan.FromMilliseconds(Math.Max(0L, _deadline - Environment.TickCount64));

    public void Dispose() => Volatile.Write(ref _closed, 1);
}
