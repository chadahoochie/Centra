using Centra.Drivers;
using Centra.Locks;
using Centra.Registry;
using Centra.Tests.Unit.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Locks;

public sealed class DistributedLockTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IDistributedLockDriver _driver = Substitute.For<IDistributedLockDriver>();
    private readonly IDistributedLock _lock = Substitute.For<IDistributedLock>();

    public DistributedLockTests()
    {
        _registry.RegisterLockDriver("lock-store", _driver);
        _lock.ResourceId.Returns("resource-1");
        _lock.LockId.Returns("lock-1");
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Acquire_Lock_When_Driver_Succeeds(
        string resourceId,
        TimeSpan expiry,
        TimeSpan timeout)
    {
        // Arrange
        _driver.AcquireLockAsync("lock-store", resourceId, expiry, timeout, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock>(_lock));

        var provider = new CentraDistributedLockProvider(_registry);

        // Act
        var acquired = await provider.AcquireLockAsync("lock-store", resourceId, expiry, timeout);

        // Assert
        acquired.ShouldNotBeNull();
        acquired.ResourceId.ShouldBe("resource-1");
        acquired.LockId.ShouldBe("lock-1");
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_TryAcquire_Lock_Returning_Null_When_Unavailable(
        string resourceId,
        TimeSpan expiry)
    {
        // Arrange
        _driver.TryAcquireLockAsync("lock-store", resourceId, expiry, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>((IDistributedLock?)null));

        var provider = new CentraDistributedLockProvider(_registry);

        // Act
        var acquired = await provider.TryAcquireLockAsync("lock-store", resourceId, expiry);

        // Assert
        acquired.ShouldBeNull();
    }
}
