using Centra.Actors;
using Centra.Core.Actors;
using Centra.Locks;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorReminderLockCoordinatorTests
{
    [Fact]
    public async Task TryAcquireReminderLockAsync_Should_Return_True_When_Lock_Provider_Is_Null()
    {
        var options = new ActorOptions();
        var coordinator = new ActorReminderLockCoordinator(null, options);
        var schedule = new ActorReminderSchedule(
            new ActorIdentity("UserActor", "u1"),
            "daily-audit",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromHours(24),
            null,
            DateTimeOffset.UtcNow);

        var (acquired, lockObj) = await coordinator.TryAcquireReminderLockAsync(schedule, CancellationToken.None);

        acquired.ShouldBeTrue();
        lockObj.ShouldBeNull();
    }

    [Fact]
    public async Task TryAcquireReminderLockAsync_Should_Handle_InvalidOperationException_Gracefully()
    {
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns<ValueTask<IDistributedLock?>>(_ => throw new InvalidOperationException("Lock store not configured"));

        var options = new ActorOptions();
        var coordinator = new ActorReminderLockCoordinator(lockProvider, options);
        var schedule = new ActorReminderSchedule(
            new ActorIdentity("UserActor", "u1"),
            "daily-audit",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromHours(24),
            null,
            DateTimeOffset.UtcNow);

        var (acquired, lockObj) = await coordinator.TryAcquireReminderLockAsync(schedule, CancellationToken.None);

        acquired.ShouldBeTrue();
        lockObj.ShouldBeNull();
    }

    [Fact]
    public async Task TryAcquireReminderLockAsync_Should_Return_Acquired_Lock_When_Successful()
    {
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var distributedLock = Substitute.For<IDistributedLock>();
        lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IDistributedLock?>(distributedLock));

        var options = new ActorOptions();
        var coordinator = new ActorReminderLockCoordinator(lockProvider, options);
        var schedule = new ActorReminderSchedule(
            new ActorIdentity("UserActor", "u1"),
            "daily-audit",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromHours(24),
            null,
            DateTimeOffset.UtcNow);

        var (acquired, lockObj) = await coordinator.TryAcquireReminderLockAsync(schedule, CancellationToken.None);

        acquired.ShouldBeTrue();
        lockObj.ShouldBe(distributedLock);
    }
}
