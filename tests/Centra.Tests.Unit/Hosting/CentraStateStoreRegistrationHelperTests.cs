using Centra.Hosting.Extensions;
using Centra.Hosting.Options;
using Centra.State;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraStateStoreRegistrationHelperTests
{
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();
    private readonly IOptions<CentraOptions> _options = Options.Create(new CentraOptions
    {
        DefaultStateStore = "custom-store"
    });

    [Fact]
    public async Task RegistrationHelper_Should_Delegate_All_Methods_To_Inner_Store()
    {
        // Arrange
        var helper = new CentraStateStoreRegistrationHelper<string>(_stateStore, _options);
        var expectedEntry = new StateEntry<string>("key1", "val1", "etag-1");

        _stateStore.GetAsync<string>("custom-store", "key1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<StateEntry<string>?>(expectedEntry));
        _stateStore.TrySetAsync("custom-store", "key1", "val1", "etag-1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));
        _stateStore.TryDeleteAsync("custom-store", "key1", "etag-1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        // Act & Assert GetAsync
        var entry = await helper.GetAsync("key1");
        entry.ShouldNotBeNull();
        entry.Value.Value.ShouldBe("val1");

        // Act & Assert SetAsync
        await helper.SetAsync("key1", "val1");
        await _stateStore.Received(1).SetAsync("custom-store", "key1", "val1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());

        // Act & Assert TrySetAsync
        var setSuccess = await helper.TrySetAsync("key1", "val1", "etag-1");
        setSuccess.ShouldBeTrue();

        // Act & Assert DeleteAsync
        await helper.DeleteAsync("key1");
        await _stateStore.Received(1).DeleteAsync("custom-store", "key1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());

        // Act & Assert TryDeleteAsync
        var delSuccess = await helper.TryDeleteAsync("key1", "etag-1");
        delSuccess.ShouldBeTrue();
    }
}
