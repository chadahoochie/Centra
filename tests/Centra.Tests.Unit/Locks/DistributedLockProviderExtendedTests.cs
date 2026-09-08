using System;
using System.Threading;
using System.Threading.Tasks;
using Centra.Drivers;
using Centra.Locks;
using Centra.Registry;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Locks;

public sealed class DistributedLockProviderExtendedTests
{
    // CentraDistributedLockProvider Tests

    [Fact]
    public void Should_Throw_ArgumentNullException_When_Registry_Is_Null()
    {
        // Arrange, Act, Assert
        Should.Throw<ArgumentNullException>(() => new CentraDistributedLockProvider(null!));
    }

    [Fact]
    public async Task Should_Throw_InvalidOperationException_When_No_Driver_Registered()
    {
        // Arrange
        var registry = new ComponentRegistry();
        var sut = new CentraDistributedLockProvider(registry);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await sut.TryAcquireLockAsync("missing_store", "resource1", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Should_Rethrow_When_TryAcquire_Driver_Throws()
    {
        // Arrange
        var registry = new ComponentRegistry();
        var mockDriver = Substitute.For<IDistributedLockDriver>();
        var exception = new Exception("Driver failed");
        mockDriver.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(exception);
        
        registry.RegisterLockDriver("store1", mockDriver);
        var sut = new CentraDistributedLockProvider(registry);

        // Act & Assert
        var ex = await Should.ThrowAsync<Exception>(async () =>
            await sut.TryAcquireLockAsync("store1", "resource1", TimeSpan.FromSeconds(10)));
        
        ex.Message.ShouldBe("Driver failed");
    }

    [Fact]
    public async Task Should_Rethrow_When_Acquire_Driver_Throws()
    {
        // Arrange
        var registry = new ComponentRegistry();
        var mockDriver = Substitute.For<IDistributedLockDriver>();
        var exception = new Exception("Driver failed");
        mockDriver.AcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(exception);
        
        registry.RegisterLockDriver("store1", mockDriver);
        var sut = new CentraDistributedLockProvider(registry);

        // Act & Assert
        var ex = await Should.ThrowAsync<Exception>(async () =>
            await sut.AcquireLockAsync("store1", "resource1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
        
        ex.Message.ShouldBe("Driver failed");
    }

    [Fact]
    public async Task Should_Return_Null_From_TryAcquire_When_Lock_Unavailable()
    {
        // Arrange
        var registry = new ComponentRegistry();
        var mockDriver = Substitute.For<IDistributedLockDriver>();
        mockDriver.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>((IDistributedLock?)null));
        
        registry.RegisterLockDriver("store1", mockDriver);
        var sut = new CentraDistributedLockProvider(registry);

        // Act
        var result = await sut.TryAcquireLockAsync("store1", "resource1", TimeSpan.FromSeconds(10));

        // Assert
        result.ShouldBeNull();
    }

    // DistributedLockHelper Tests

    [Fact]
    public async Task Should_Acquire_Lock_On_First_Attempt()
    {
        // Arrange
        var provider = Substitute.For<IDistributedLockProvider>();
        var expectedLock = Substitute.For<IDistributedLock>();
        provider.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>(expectedLock));

        // Act
        var result = await DistributedLockHelper.AcquireLockAsync(provider, "store1", "res1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

        // Assert
        result.ShouldBeSameAs(expectedLock);
        await provider.Received(1).TryAcquireLockAsync("store1", "res1", TimeSpan.FromSeconds(10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Throw_TimeoutException_When_Lock_Not_Acquired_Within_Timeout()
    {
        // Arrange
        var provider = Substitute.For<IDistributedLockProvider>();
        provider.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>((IDistributedLock?)null));

        // Act & Assert
        var ex = await Should.ThrowAsync<TimeoutException>(async () =>
            await DistributedLockHelper.AcquireLockAsync(provider, "store1", "res1", TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(1)));
        
        ex.Message.ShouldContain("Failed to acquire lock");
    }

    [Fact]
    public async Task Should_Throw_ArgumentNullException_When_Provider_Is_Null()
    {
        // Arrange, Act, Assert
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await DistributedLockHelper.AcquireLockAsync(null!, "store1", "res1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Should_Throw_ArgumentException_When_LockStoreName_Is_Empty(string? invalidName)
    {
        // Arrange
        var provider = Substitute.For<IDistributedLockProvider>();

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(async () =>
            await DistributedLockHelper.AcquireLockAsync(provider, invalidName!, "res1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Should_Throw_ArgumentException_When_ResourceId_Is_Empty(string? invalidId)
    {
        // Arrange
        var provider = Substitute.For<IDistributedLockProvider>();

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(async () =>
            await DistributedLockHelper.AcquireLockAsync(provider, "store1", invalidId!, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Should_Throw_OperationCanceledException_When_Token_Cancelled()
    {
        // Arrange
        var provider = Substitute.For<IDistributedLockProvider>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await DistributedLockHelper.AcquireLockAsync(provider, "store1", "res1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), cts.Token));
    }
}
