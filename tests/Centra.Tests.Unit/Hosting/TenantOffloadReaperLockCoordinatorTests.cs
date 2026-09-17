using Centra.Hosting.HostedServices;
using Centra.Locks;
using Centra.PubSub.Tenancy;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class TenantOffloadReaperLockCoordinatorTests
{
    [Fact]
    public async Task TryAcquireReaperLockAsync_ReturnsTrue_When_LockProviderIsNull()
    {
        var options = new TenantOffloadOptions { EnableDistributedReaperLock = true };
        var coordinator = new TenantOffloadReaperLockCoordinator(lockProvider: null, options);

        var (acquired, @lock) = await coordinator.TryAcquireReaperLockAsync(CancellationToken.None);

        Assert.True(acquired);
        Assert.Null(@lock);
    }

    [Fact]
    public async Task TryAcquireReaperLockAsync_ReturnsTrue_When_DistributedLockIsDisabledInOptions()
    {
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var options = new TenantOffloadOptions { EnableDistributedReaperLock = false };
        var coordinator = new TenantOffloadReaperLockCoordinator(lockProvider, options);

        var (acquired, @lock) = await coordinator.TryAcquireReaperLockAsync(CancellationToken.None);

        Assert.True(acquired);
        Assert.Null(@lock);
        await lockProvider.DidNotReceiveWithAnyArgs().TryAcquireLockAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task TryAcquireReaperLockAsync_ReturnsTrueAndLock_When_LockAcquisitionSucceeds()
    {
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var distLock = Substitute.For<IDistributedLock>();
        var options = new TenantOffloadOptions { EnableDistributedReaperLock = true };

        lockProvider.TryAcquireLockAsync("lockstore", "centra:reaper:tenant-offload", TimeSpan.FromSeconds(15), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IDistributedLock?>(distLock));

        var coordinator = new TenantOffloadReaperLockCoordinator(lockProvider, options);

        var (acquired, @lock) = await coordinator.TryAcquireReaperLockAsync(CancellationToken.None);

        Assert.True(acquired);
        Assert.Same(distLock, @lock);
    }

    [Fact]
    public async Task TryAcquireReaperLockAsync_ReturnsFalse_When_LockAcquisitionFails()
    {
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var options = new TenantOffloadOptions { EnableDistributedReaperLock = true };

        lockProvider.TryAcquireLockAsync("lockstore", "centra:reaper:tenant-offload", TimeSpan.FromSeconds(15), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IDistributedLock?>(null));

        var coordinator = new TenantOffloadReaperLockCoordinator(lockProvider, options);

        var (acquired, @lock) = await coordinator.TryAcquireReaperLockAsync(CancellationToken.None);

        Assert.False(acquired);
        Assert.Null(@lock);
    }

    [Fact]
    public async Task TryAcquireReaperLockAsync_ReturnsFalse_When_LockProviderThrows()
    {
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var options = new TenantOffloadOptions { EnableDistributedReaperLock = true };

        lockProvider.TryAcquireLockAsync("lockstore", "centra:reaper:tenant-offload", TimeSpan.FromSeconds(15), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Redis timed out"));

        var coordinator = new TenantOffloadReaperLockCoordinator(lockProvider, options);

        var (acquired, @lock) = await coordinator.TryAcquireReaperLockAsync(CancellationToken.None);

        Assert.False(acquired);
        Assert.Null(@lock);
    }
}
