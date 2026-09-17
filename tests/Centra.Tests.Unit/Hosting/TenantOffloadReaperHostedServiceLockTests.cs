using Centra.Hosting.HostedServices;
using Centra.Locks;
using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class TenantOffloadReaperHostedServiceLockTests
{
    [Fact]
    public async Task ExecuteAsync_WithLockAcquired_ExecutesCleanupAndDisposesLock()
    {
        var coordinator = Substitute.For<ITenantOffloadCoordinator>();
        var lockCoordinator = Substitute.For<ITenantOffloadReaperLockCoordinator>();
        var distLock = Substitute.For<IDistributedLock>();
        var logger = Substitute.For<ILogger<TenantOffloadReaperHostedService>>();

        lockCoordinator.TryAcquireReaperLockAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<(bool, IDistributedLock?)>((true, distLock)));

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

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await service.StartAsync(cts.Token);
        await Task.Delay(40);
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
        lockCoordinator.TryAcquireReaperLockAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<(bool, IDistributedLock?)>((false, null)));

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

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await service.StartAsync(cts.Token);
        await Task.Delay(40);
        await service.StopAsync(CancellationToken.None);

        await coordinator.DidNotReceive().CleanupIdleResourcesAsync(Arg.Any<CancellationToken>());
    }
}
