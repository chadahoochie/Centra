using System.Collections.Concurrent;
using Centra.Bindings;
using Microsoft.Extensions.Logging;

namespace Centra.Bindings;

public sealed class CentraCronScheduler : IScheduler, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CentraCronScheduler>? _logger;
    private readonly ConcurrentDictionary<string, ScheduledJobEntry> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public CentraCronScheduler(
        TimeProvider? timeProvider = null,
        ILogger<CentraCronScheduler>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public void ScheduleCron(
        string jobName,
        string cronExpression,
        IJobHandler handler,
        CronScheduleOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentNullException.ThrowIfNull(handler);

        ThrowIfDisposed();

        var parser = CronExpressionParser.Parse(cronExpression);
        var entry = new ScheduledJobEntry(this, jobName, handler, parser, null, options ?? new CronScheduleOptions());

        AddOrUpdateJob(entry);
    }

    public void ScheduleInterval(
        string jobName,
        TimeSpan interval,
        IJobHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(handler);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");
        }

        ThrowIfDisposed();

        var entry = new ScheduledJobEntry(this, jobName, handler, null, interval, new CronScheduleOptions());

        AddOrUpdateJob(entry);
    }

    public bool Unregister(string jobName)
    {
        if (_jobs.TryRemove(jobName, out var entry))
        {
            entry.Dispose();
            return true;
        }

        return false;
    }

    private void AddOrUpdateJob(ScheduledJobEntry entry)
    {
        _jobs.AddOrUpdate(
            entry.JobName,
            addValueFactory: _ =>
            {
                ScheduleNext(entry);
                return entry;
            },
            updateValueFactory: (_, existing) =>
            {
                existing.Dispose();
                ScheduleNext(entry);
                return entry;
            });
    }

    private void ScheduleNext(ScheduledJobEntry entry)
    {
        if (_disposed || entry.IsDisposed) return;

        var now = _timeProvider.GetUtcNow();
        DateTimeOffset? nextOccurrence;

        if (entry.Interval.HasValue)
        {
            nextOccurrence = now.Add(entry.Interval.Value);
        }
        else if (entry.Parser is not null)
        {
            nextOccurrence = entry.Parser.GetNextOccurrence(now);
        }
        else
        {
            return;
        }

        if (!nextOccurrence.HasValue)
        {
            return;
        }

        var dueTime = nextOccurrence.Value - now;
        if (dueTime < TimeSpan.Zero) dueTime = TimeSpan.Zero;

        entry.ScheduledTime = nextOccurrence.Value;

        if (entry.Timer is null)
        {
            entry.Timer = _timeProvider.CreateTimer(
                callback: static state =>
                {
                    var jobEntry = (ScheduledJobEntry)state!;
                    _ = jobEntry.Scheduler.ExecuteJobTickAsync(jobEntry);
                },
                state: entry,
                dueTime: dueTime,
                period: Timeout.InfiniteTimeSpan);
        }
        else
        {
            entry.Timer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ExecuteJobTickAsync(ScheduledJobEntry entry)
    {
        if (_disposed || entry.IsDisposed) return;

        var scheduledTime = entry.ScheduledTime;
        var actualTime = _timeProvider.GetUtcNow();
        var iteration = Interlocked.Increment(ref entry.Iteration);

        var context = new ScheduledJobContext(
            entry.JobName,
            scheduledTime,
            actualTime,
            iteration,
            entry.Cts.Token);

        try
        {
            await entry.Handler.ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Scheduled job '{JobName}' failed on iteration {Iteration}.", entry.JobName, iteration);
        }
        finally
        {
            ScheduleNext(entry);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var entry in _jobs.Values)
        {
            entry.Dispose();
        }

        _jobs.Clear();
    }

    private sealed class ScheduledJobEntry(
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
}
