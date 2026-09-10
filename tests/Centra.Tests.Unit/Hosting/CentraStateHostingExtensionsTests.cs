using Centra.Hosting.Extensions;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraStateHostingExtensionsTests
{
    private sealed record TestStateItem(string Id, string Value);

    [Fact]
    public void AddCentraStateStore_Should_Register_And_Resolve_Typed_StateStore()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockStateStore = Substitute.For<IStateStore>();
        services.AddSingleton(mockStateStore);

        // Act
        services.AddCentraStateStore<TestStateItem>("orders-store");
        var provider = services.BuildServiceProvider();
        var typedStore = provider.GetService<IStateStore<TestStateItem>>();

        // Assert
        typedStore.ShouldNotBeNull();
        typedStore.ShouldBeOfType<CentraStateStore<TestStateItem>>();
    }
}
