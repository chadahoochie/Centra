namespace Centra.Bindings;

internal sealed class ScheduledJobEntry(
    CentraCronScheduler scheduler,
    string jobName,
    IJobHandler handler,
    CronExpressionParser? parser,
    TimeSpan? interval,
    CronScheduleOptions options) : IDisposable
{
    public CentraCronScheduler Scheduler { get; } = scheduler;
    public string JobName { get; } = jobName;
    public IJobHandler Handler { get; } = handler;
    public CronExpressionParser? Parser { get; } = parser;
    public TimeSpan? Interval { get; } = interval;
    public CronScheduleOptions Options { get; } = options;
    public ITimer? Timer { get; set; }
    public DateTimeOffset ScheduledTime { get; set; }
    public long Iteration;
    public CancellationTokenSource Cts { get; } = new();
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Cts.Cancel();
        Timer?.Dispose();
    }
}
