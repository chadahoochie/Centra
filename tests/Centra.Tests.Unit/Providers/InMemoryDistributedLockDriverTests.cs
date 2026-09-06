using Centra.Locks;
using Centra.Providers.InMemory.Locks;
using Centra.Tests.Unit.Common;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Providers;

public sealed class InMemoryDistributedLockDriverTests
{
    private readonly InMemoryDistributedLockDriver _driver = new();

    [Theory, AutoNSubstituteData]
    public async Task Should_Acquire_And_Release_Lock_Successfully(string store, string resource)
    {
        // Act 1: Acquire
        var lock1 = await _driver.TryAcquireLockAsync(store, resource, TimeSpan.FromSeconds(10));

        // Assert 1
        lock1.ShouldNotBeNull();
        lock1.ResourceId.ShouldBe(resource);

        // Act 2: Second acquisition should fail
        var lock2 = await _driver.TryAcquireLockAsync(store, resource, TimeSpan.FromSeconds(10));
        lock2.ShouldBeNull();

        // Act 3: Release lock 1
        await lock1.DisposeAsync();

        // Act 4: Now acquisition should succeed
        var lock3 = await _driver.TryAcquireLockAsync(store, resource, TimeSpan.FromSeconds(10));
        lock3.ShouldNotBeNull();
        await lock3.DisposeAsync();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Renew_Lock_Successfully(string store, string resource)
    {
        // Arrange
        await using var @lock = await _driver.TryAcquireLockAsync(store, resource, TimeSpan.FromSeconds(5));
        @lock.ShouldNotBeNull();

        // Act
        var renewed = await @lock.RenewAsync(TimeSpan.FromSeconds(10));

        // Assert
        renewed.ShouldBeTrue();
    }
}
