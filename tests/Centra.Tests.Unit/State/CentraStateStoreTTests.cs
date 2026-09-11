using Centra.State;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.State;

public sealed class CentraStateStoreTTests
{
    private readonly IStateStore _innerStore = Substitute.For<IStateStore>();
    private const string StoreName = "my-store";

    [Fact]
    public void Constructor_NullInnerStore_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CentraStateStore<string>(null!, StoreName));
    }

    [Fact]
    public void Constructor_NullStoreName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CentraStateStore<string>(_innerStore, null!));
    }

    [Fact]
    public async Task Methods_DelegateToInnerStoreCorrectly()
    {
        var sut = new CentraStateStore<string>(_innerStore, StoreName);
        var expectedEntry = new StateEntry<string>("k1", "v1", "etag1");

        _innerStore.GetAsync<string>(StoreName, "k1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<StateEntry<string>?>(expectedEntry));
        _innerStore.TrySetAsync(StoreName, "k1", "v1", "etag1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));
        _innerStore.TryDeleteAsync(StoreName, "k1", "etag1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        var entry = await sut.GetAsync("k1");
        entry.ShouldBe(expectedEntry);

        await sut.SetAsync("k1", "v1");
        await _innerStore.Received(1).SetAsync(StoreName, "k1", "v1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());

        var trySet = await sut.TrySetAsync("k1", "v1", "etag1");
        trySet.ShouldBeTrue();

        await sut.DeleteAsync("k1");
        await _innerStore.Received(1).DeleteAsync(StoreName, "k1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());

        var tryDelete = await sut.TryDeleteAsync("k1", "etag1");
        tryDelete.ShouldBeTrue();
    }
}
