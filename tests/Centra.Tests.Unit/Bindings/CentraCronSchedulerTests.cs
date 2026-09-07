using Centra.Bindings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class CentraCronSchedulerTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly CentraCronScheduler _scheduler;

    public CentraCronSchedulerTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero));
        _scheduler = new CentraCronScheduler(_timeProvider, NullLogger<CentraCronScheduler>.Instance);
    }

    [Fact]
    public async Task ScheduleCron_WhenTimeAdvances_ShouldTriggerJob()
    {
        var executedCount = 0;
        ScheduledJobContext capturedContext = default;
        var handler = new TestJobHandler(ctx =>
        {
            Interlocked.Increment(ref executedCount);
            capturedContext = ctx;
            return ValueTask.CompletedTask;
        });

        // Schedule every minute
        _scheduler.ScheduleCron("minutely-job", "* * * * *", handler);

        // Advance 30 seconds: should not fire yet
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        var firedEarly = await handler.WaitForExecutionAsync(TimeSpan.FromMilliseconds(50));
        firedEarly.ShouldBeFalse();
        executedCount.ShouldBe(0);

        // Advance another 31 seconds: crossed 10:01:00, should fire exactly once
        _timeProvider.Advance(TimeSpan.FromSeconds(31));
        var fired = await handler.WaitForExecutionAsync(TimeSpan.FromSeconds(2));

        fired.ShouldBeTrue();
        executedCount.ShouldBe(1);
        capturedContext.JobName.ShouldBe("minutely-job");
        capturedContext.Iteration.ShouldBe(1);
        capturedContext.ScheduledTime.ShouldBe(new DateTimeOffset(2026, 9, 7, 10, 1, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ScheduleCron_MultipleOccurrences_ShouldIncrementIteration()
    {
        var iterations = new List<long>();
        var handler = new TestJobHandler(ctx =>
        {
            lock (iterations) iterations.Add(ctx.Iteration);
            return ValueTask.CompletedTask;
        });

        // Schedule every 5 seconds
        _scheduler.ScheduleCron("five-sec-job", "@every 5s", handler);

        for (var i = 1; i <= 3; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(5));
            var fired = await handler.WaitForExecutionAsync(TimeSpan.FromSeconds(2));
            fired.ShouldBeTrue();
        }

        lock (iterations)
        {
            iterations.Count.ShouldBe(3);
            iterations.ShouldBe([1, 2, 3]);
        }
    }

    [Fact]
    public async Task ScheduleInterval_ShouldTriggerRegularly()
    {
        var runCount = 0;
        var handler = new TestJobHandler(_ =>
        {
            Interlocked.Increment(ref runCount);
            return ValueTask.CompletedTask;
        });

        _scheduler.ScheduleInterval("heartbeat", TimeSpan.FromSeconds(10), handler);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var fired1 = await handler.WaitForExecutionAsync(TimeSpan.FromSeconds(2));
        fired1.ShouldBeTrue();
        runCount.ShouldBe(1);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var fired2 = await handler.WaitForExecutionAsync(TimeSpan.FromSeconds(2));
        fired2.ShouldBeTrue();
        runCount.ShouldBe(2);
    }

    [Fact]
    public async Task Unregister_ShouldStopJobFromExecuting()
    {
        var runCount = 0;
        var handler = new TestJobHandler(_ =>
        {
            Interlocked.Increment(ref runCount);
            return ValueTask.CompletedTask;
        });

        _scheduler.ScheduleInterval("temp-job", TimeSpan.FromSeconds(10), handler);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var fired = await handler.WaitForExecutionAsync(TimeSpan.FromSeconds(2));
        fired.ShouldBeTrue();
        runCount.ShouldBe(1);

        var removed = _scheduler.Unregister("temp-job");
        removed.ShouldBeTrue();

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var firedAfter = await handler.WaitForExecutionAsync(TimeSpan.FromMilliseconds(50));
        firedAfter.ShouldBeFalse();
        runCount.ShouldBe(1); // Did not increase
    }

    [Fact]
    public async Task ScheduleCron_WhenHandlerThrows_ShouldNotCrashScheduler()
    {
        var callCount = 0;
        var handler = new TestJobHandler(_ =>
        {
            var current = Interlocked.Increment(ref callCount);
            if (current == 1)
            {
                throw new InvalidOperationException("Simulated job failure");
            }
            return ValueTask.CompletedTask;
        });

        _scheduler.ScheduleCron("failing-job", "* * * * *", handler);

        // First tick throws
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var fired1 = await handler.WaitForExecutionAsync(TimeSpan.FromSeconds(2));
        fired1.ShouldBeTrue();
        callCount.ShouldBe(1);

        // Next tick must continue working
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var fired2 = await handler.WaitForExecutionAsync(TimeSpan.FromSeconds(2));
        fired2.ShouldBeTrue();
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task ScheduleCron_SameJobNameTwice_ShouldReplacePreviousJob()
    {
        var job1Runs = 0;
        var job2Runs = 0;

        var handler1 = new TestJobHandler(_ =>
        {
            Interlocked.Increment(ref job1Runs);
            return ValueTask.CompletedTask;
        });

        var handler2 = new TestJobHandler(_ =>
        {
            Interlocked.Increment(ref job2Runs);
            return ValueTask.CompletedTask;
        });

        _scheduler.ScheduleInterval("job-x", TimeSpan.FromSeconds(5), handler1);

        // Replace with new interval
        _scheduler.ScheduleInterval("job-x", TimeSpan.FromSeconds(10), handler2);

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        var fired1 = await handler1.WaitForExecutionAsync(TimeSpan.FromMilliseconds(50));
        var fired2Early = await handler2.WaitForExecutionAsync(TimeSpan.FromMilliseconds(50));
        fired1.ShouldBeFalse();
        fired2Early.ShouldBeFalse();
        job1Runs.ShouldBe(0); // Job 1 was canceled/replaced
        job2Runs.ShouldBe(0); // Job 2 interval is 10s

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        var fired2 = await handler2.WaitForExecutionAsync(TimeSpan.FromSeconds(2));
        fired2.ShouldBeTrue();
        job1Runs.ShouldBe(0);
        job2Runs.ShouldBe(1);
    }

    [Fact]
    public void Unregister_NonExistentJob_ReturnsFalse()
    {
        _scheduler.Unregister("does-not-exist").ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ScheduleInterval_ZeroOrNegativeInterval_ThrowsArgumentOutOfRangeException(int seconds)
    {
        var handler = new TestJobHandler(_ => ValueTask.CompletedTask);
        Should.Throw<ArgumentOutOfRangeException>(() =>
            _scheduler.ScheduleInterval("invalid-interval", TimeSpan.FromSeconds(seconds), handler));
    }

    [Fact]
    public void ScheduleCron_WhenDisposed_ThrowsObjectDisposedException()
    {
        var handler = new TestJobHandler(_ => ValueTask.CompletedTask);
        _scheduler.Dispose();

        Should.Throw<ObjectDisposedException>(() =>
            _scheduler.ScheduleCron("disposed-job", "* * * * *", handler));
    }

    private sealed class TestJobHandler(Func<ScheduledJobContext, ValueTask> action) : IJobHandler
    {
        private readonly SemaphoreSlim _semaphore = new(0);

        public async ValueTask ExecuteAsync(ScheduledJobContext context)
        {
            try
            {
                await action(context);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool> WaitForExecutionAsync(TimeSpan timeout)
        {
            return await _semaphore.WaitAsync(timeout);
        }
    }
}
