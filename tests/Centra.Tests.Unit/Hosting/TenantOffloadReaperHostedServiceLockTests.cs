using Centra.Hosting.HostedServices;
using Centra.Locks;
using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class TenantOffloadReaperHostedServiceLockTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ExecuteAsync_WithLockAcquired_ExecutesCleanupAndDisposesLock()
    {
        var coordinator = Substitute.For<ITenantOffloadCoordinator>();
        var lockCoordinator = Substitute.For<ITenantOffloadReaperLockCoordinator>();
        var distLock = Substitute.For<IDistributedLock>();
        var logger = Substitute.For<ILogger<TenantOffloadReaperHostedService>>();

        lockCoordinator.TryAcquireReaperLockAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<(bool, IDistributedLock?)>((true, distLock)));

        // The reaper disposes the lock only after cleanup returns, so the dispose
        // signal deterministically marks a completed tick without timing guesses.
        var lockReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        distLock.When(l => l.DisposeAsync()).Do(_ => lockReleased.TrySetResult());

        var options = Microsoft.Extensions.Options.Options.Create(new TenantOffloadOptions
        {
            LaneIdleTimeout = TimeSpan.FromMilliseconds(20)
        });

        var service = new TenantOffloadReaperHostedService(
            coordinator,
            options,
            logger,
            lockProvider: null,
            lockCoordinator: lockCoordinator);

        await service.StartAsync(CancellationToken.None);
        await lockReleased.Task.WaitAsync(SignalTimeout);
        await service.StopAsync(CancellationToken.None);

        await coordinator.Received().CleanupIdleResourcesAsync(Arg.Any<CancellationToken>());
        await distLock.Received().DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenLockCannotBeAcquired_SkipsCleanup()
    {
        var coordinator = Substitute.For<ITenantOffloadCoordinator>();
        var lockCoordinator = Substitute.For<ITenantOffloadReaperLockCoordinator>();
        var logger = Substitute.For<ILogger<TenantOffloadReaperHostedService>>();

        // Lock acquisition fails (held by another replica)
        var lockAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lockCoordinator.TryAcquireReaperLockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lockAttempted.TrySetResult();
                return ValueTask.FromResult<(bool, IDistributedLock?)>((false, null));
            });

        var options = Microsoft.Extensions.Options.Options.Create(new TenantOffloadOptions
        {
            LaneIdleTimeout = TimeSpan.FromMilliseconds(20)
        });

        var service = new TenantOffloadReaperHostedService(
            coordinator,
            options,
            logger,
            lockProvider: null,
            lockCoordinator: lockCoordinator);

        await service.StartAsync(CancellationToken.None);
        await lockAttempted.Task.WaitAsync(SignalTimeout);
        await service.StopAsync(CancellationToken.None);

        await coordinator.DidNotReceive().CleanupIdleResourcesAsync(Arg.Any<CancellationToken>());
    }
}
