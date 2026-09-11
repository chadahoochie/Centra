using Centra.Core.Workflows;
using Centra.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowTimerSchedulerTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public void ScheduleTimer_NullCallback_ThrowsArgumentNullException()
    {
        using var scheduler = new WorkflowTimerScheduler(_timeProvider, _logger);
        var instanceId = new WorkflowInstanceId("wf-1");

        Should.Throw<ArgumentNullException>(() =>
            scheduler.ScheduleTimer(instanceId, TimeSpan.FromSeconds(5), null!));
    }

    [Fact]
    public void ScheduleTimer_FiresCallback_WhenTimeAdvances()
    {
        using var scheduler = new WorkflowTimerScheduler(_timeProvider, _logger);
        var instanceId = new WorkflowInstanceId("wf-2");
        var fired = false;
        WorkflowInstanceId? firedId = null;

        scheduler.ScheduleTimer(instanceId, TimeSpan.FromSeconds(10), id =>
        {
            fired = true;
            firedId = id;
            return ValueTask.CompletedTask;
        });

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        fired.ShouldBeFalse();

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        fired.ShouldBeTrue();
        firedId.ShouldBe(instanceId);
    }

    [Fact]
    public void ScheduleTimer_WhenCallbackThrows_LogsErrorAndDoesNotCrash()
    {
        using var scheduler = new WorkflowTimerScheduler(_timeProvider, _logger);
        var instanceId = new WorkflowInstanceId("wf-error");

        scheduler.ScheduleTimer(instanceId, TimeSpan.FromSeconds(2), _ =>
            throw new InvalidOperationException("Timer callback boom"));

        // Advance time - should catch and log error
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        _logger.ReceivedWithAnyArgs().Log(
            LogLevel.Error,
            default,
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void ScheduleTimer_ReschedulingReplacesAndDisposesPreviousTimer()
    {
        using var scheduler = new WorkflowTimerScheduler(_timeProvider, _logger);
        var instanceId = new WorkflowInstanceId("wf-replace");
        var firstCount = 0;
        var secondCount = 0;

        scheduler.ScheduleTimer(instanceId, TimeSpan.FromSeconds(10), _ =>
        {
            firstCount++;
            return ValueTask.CompletedTask;
        });

        // Reschedule before first fires
        scheduler.ScheduleTimer(instanceId, TimeSpan.FromSeconds(20), _ =>
        {
            secondCount++;
            return ValueTask.CompletedTask;
        });

        _timeProvider.Advance(TimeSpan.FromSeconds(15));
        firstCount.ShouldBe(0); // old timer was disposed
        secondCount.ShouldBe(0); // new timer not yet due

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        firstCount.ShouldBe(0);
        secondCount.ShouldBe(1);
    }

    [Fact]
    public void CancelTimer_WhenActive_ReturnsTrue_AndPreventsFiring()
    {
        using var scheduler = new WorkflowTimerScheduler(_timeProvider, _logger);
        var instanceId = new WorkflowInstanceId("wf-cancel");
        var fired = false;

        scheduler.ScheduleTimer(instanceId, TimeSpan.FromSeconds(5), _ =>
        {
            fired = true;
            return ValueTask.CompletedTask;
        });

        var cancelled = scheduler.CancelTimer(instanceId);
        cancelled.ShouldBeTrue();

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        fired.ShouldBeFalse();
    }

    [Fact]
    public void CancelTimer_WhenNotActive_ReturnsFalse()
    {
        using var scheduler = new WorkflowTimerScheduler(_timeProvider, _logger);
        var instanceId = new WorkflowInstanceId("wf-unknown");

        var cancelled = scheduler.CancelTimer(instanceId);
        cancelled.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_DisposesAllActiveTimers()
    {
        var scheduler = new WorkflowTimerScheduler(_timeProvider, _logger);
        var firedCount = 0;

        scheduler.ScheduleTimer(new WorkflowInstanceId("wf-1"), TimeSpan.FromSeconds(5), _ =>
        {
            firedCount++;
            return ValueTask.CompletedTask;
        });
        scheduler.ScheduleTimer(new WorkflowInstanceId("wf-2"), TimeSpan.FromSeconds(5), _ =>
        {
            firedCount++;
            return ValueTask.CompletedTask;
        });

        scheduler.Dispose();

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        firedCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_DefaultTimeProvider_InstantiatesSuccessfully()
    {
        using var defaultScheduler = new WorkflowTimerScheduler();
        var cancelled = defaultScheduler.CancelTimer(new WorkflowInstanceId("none"));
        cancelled.ShouldBeFalse();
    }
}
