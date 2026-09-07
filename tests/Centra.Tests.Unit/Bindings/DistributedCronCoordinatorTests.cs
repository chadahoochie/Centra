using Centra.Bindings;
using Centra.Locks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class DistributedCronCoordinatorTests
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IJobHandler _innerHandler;

    public DistributedCronCoordinatorTests()
    {
        _lockProvider = Substitute.For<IDistributedLockProvider>();
        _innerHandler = Substitute.For<IJobHandler>();
    }

    [Fact]
    public void Constructor_NullArguments_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new DistributedJobHandler(null!, _lockProvider));
        Should.Throw<ArgumentNullException>(() => new DistributedJobHandler(_innerHandler, null!));
    }

    [Fact]
    public async Task ExecuteAsync_WhenLockAcquired_ShouldExecuteInnerHandlerAndDisposeLock()
    {
        var lockObj = Substitute.For<IDistributedLock>();

        _lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>(lockObj));

        var coordinator = new DistributedJobHandler(_innerHandler, _lockProvider, "lockstore", NullLogger<DistributedJobHandler>.Instance);

        var scheduledTime = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var context = new ScheduledJobContext("report-job", scheduledTime, scheduledTime, 1, CancellationToken.None);

        await coordinator.ExecuteAsync(context);

        await _innerHandler.Received(1).ExecuteAsync(context);
        await lockObj.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenLockCannotBeAcquired_ShouldSkipInnerHandler()
    {
        _lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>((IDistributedLock?)null));

        var coordinator = new DistributedJobHandler(_innerHandler, _lockProvider, "lockstore", NullLogger<DistributedJobHandler>.Instance);

        var scheduledTime = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var context = new ScheduledJobContext("report-job", scheduledTime, scheduledTime, 1, CancellationToken.None);

        await coordinator.ExecuteAsync(context);

        await _innerHandler.DidNotReceive().ExecuteAsync(Arg.Any<ScheduledJobContext>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenInnerHandlerThrows_ShouldStillDisposeLock()
    {
        var lockObj = Substitute.For<IDistributedLock>();

        _lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>(lockObj));

        _innerHandler.ExecuteAsync(Arg.Any<ScheduledJobContext>())
            .Returns(_ => throw new InvalidOperationException("Boom!"));

        var coordinator = new DistributedJobHandler(_innerHandler, _lockProvider, "lockstore", NullLogger<DistributedJobHandler>.Instance);

        var scheduledTime = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var context = new ScheduledJobContext("report-job", scheduledTime, scheduledTime, 1, CancellationToken.None);

        await Should.ThrowAsync<InvalidOperationException>(async () => await coordinator.ExecuteAsync(context));

        await lockObj.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_ResourceId_ShouldIncorporateJobNameAndTimestamp()
    {
        string? capturedResourceId = null;
        var lockObj = Substitute.For<IDistributedLock>();

        _lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Do<string>(id => capturedResourceId = id),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>(lockObj));

        var coordinator = new DistributedJobHandler(_innerHandler, _lockProvider, "custom-locks", NullLogger<DistributedJobHandler>.Instance);

        var scheduledTime = new DateTimeOffset(2026, 9, 7, 14, 30, 0, TimeSpan.Zero);
        var context = new ScheduledJobContext("sync-job", scheduledTime, scheduledTime, 5, CancellationToken.None);

        await coordinator.ExecuteAsync(context);

        capturedResourceId.ShouldBe($"cron:sync-job:{scheduledTime.ToUnixTimeSeconds()}");
    }
}
