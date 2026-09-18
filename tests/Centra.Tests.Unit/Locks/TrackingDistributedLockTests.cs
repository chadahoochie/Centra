using Centra.Locks;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Locks;

public sealed class TrackingDistributedLockTests
{
    [Fact]
    public void Constructor_NullArguments_ShouldThrow()
    {
        var innerLock = Substitute.For<IDistributedLock>();
        Should.Throw<ArgumentNullException>(() => new TrackingDistributedLock(null!, "lockstore"));
        Should.Throw<ArgumentException>(() => new TrackingDistributedLock(innerLock, null!));
        Should.Throw<ArgumentException>(() => new TrackingDistributedLock(innerLock, "  "));
    }

    [Fact]
    public void Properties_ShouldDelegateToInnerLock()
    {
        var innerLock = Substitute.For<IDistributedLock>();
        innerLock.ResourceId.Returns("res-1");
        innerLock.LockId.Returns("lock-1");

        var trackingLock = new TrackingDistributedLock(innerLock, "lockstore");

        trackingLock.ResourceId.ShouldBe("res-1");
        trackingLock.LockId.ShouldBe("lock-1");
    }

    [Fact]
    public async Task RenewAsync_ShouldDelegateToInnerLock()
    {
        var innerLock = Substitute.For<IDistributedLock>();
        innerLock.RenewAsync(TimeSpan.FromSeconds(30), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        var trackingLock = new TrackingDistributedLock(innerLock, "lockstore");

        var renewed = await trackingLock.RenewAsync(TimeSpan.FromSeconds(30));

        renewed.ShouldBeTrue();
        await innerLock.Received(1).RenewAsync(TimeSpan.FromSeconds(30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeInnerLockOnce()
    {
        var innerLock = Substitute.For<IDistributedLock>();
        var trackingLock = new TrackingDistributedLock(innerLock, "lockstore");

        await trackingLock.DisposeAsync();
        await trackingLock.DisposeAsync();

        await innerLock.Received(1).DisposeAsync();
    }
}
