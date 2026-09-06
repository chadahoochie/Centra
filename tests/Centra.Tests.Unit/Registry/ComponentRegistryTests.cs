using Centra.Components;
using Centra.Drivers;
using Centra.Registry;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Registry;

public sealed class ComponentRegistryTests
{
    [Fact]
    public void Should_Fire_ComponentUpdated_When_Component_Is_Registered()
    {
        // Arrange
        var registry = new ComponentRegistry();
        ComponentDefinition? captured = null;
        registry.ComponentUpdated += def => captured = def;

        var definition = new ComponentDefinition
        {
            Name = "my-store",
            Type = ComponentType.StateStore,
            Provider = "in-memory"
        };

        // Act
        registry.RegisterComponent(definition);

        // Assert
        captured.ShouldNotBeNull();
        captured.Name.ShouldBe("my-store");
        registry.GetComponent("my-store").ShouldBe(definition);
    }

    [Fact]
    public void Should_Remove_Component_And_Associated_Drivers_And_Fire_Event()
    {
        // Arrange
        var registry = new ComponentRegistry();
        string? removedName = null;
        registry.ComponentRemoved += name => removedName = name;

        var definition = new ComponentDefinition
        {
            Name = "my-store",
            Type = ComponentType.StateStore,
            Provider = "in-memory"
        };
        var mockDriver = Substitute.For<IStateStoreDriver>();

        registry.RegisterComponent(definition);
        registry.RegisterStateStoreDriver("my-store", mockDriver);

        // Act
        registry.RemoveComponent("my-store");

        // Assert
        removedName.ShouldBe("my-store");
        registry.GetComponent("my-store").ShouldBeNull();
        registry.GetStateStoreDriver("my-store").ShouldBeNull();
    }
}
