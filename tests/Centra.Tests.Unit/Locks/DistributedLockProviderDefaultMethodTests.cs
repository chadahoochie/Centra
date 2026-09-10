using System;
using System.Threading;
using System.Threading.Tasks;
using Centra.Locks;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Locks;

public sealed class DistributedLockProviderDefaultMethodTests
{
    private sealed class MinimalLockProvider : IDistributedLockProvider
    {
        private readonly IDistributedLock? _lockToReturn;

        public MinimalLockProvider(IDistributedLock? lockToReturn)
        {
            _lockToReturn = lockToReturn;
        }

        public ValueTask<IDistributedLock?> TryAcquireLockAsync(
            string lockStoreName,
            string resourceId,
            TimeSpan expiryTime,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IDistributedLock?>(_lockToReturn);
        }
    }

    [Fact]
    public async Task AcquireLockAsync_DefaultImplementation_Should_Call_Helper_And_Succeed()
    {
        // Arrange
        var mockLock = Substitute.For<IDistributedLock>();
        IDistributedLockProvider sut = new MinimalLockProvider(mockLock);

        // Act
        var acquired = await sut.AcquireLockAsync(
            "test-store",
            "test-resource",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(2));

        // Assert
        acquired.ShouldBeSameAs(mockLock);
    }
}
