using System;
using Centra.Providers.Redis.Extensions;
using Centra.Registry;
using Shouldly;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.Extensions;

public sealed class RedisDelegateComponentInitializerTests
{
    [Fact]
    public void Constructor_Should_Throw_ArgumentNullException_When_Action_Is_Null()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new RedisDelegateComponentInitializer(null!));
    }

    [Fact]
    public void Initialize_Should_Invoke_Configured_Action()
    {
        // Arrange
        var invoked = false;
        ComponentRegistry? passedRegistry = null;
        var sut = new RedisDelegateComponentInitializer(reg =>
        {
            invoked = true;
            passedRegistry = reg;
        });
        var registry = new ComponentRegistry();

        // Act
        sut.Initialize(registry);

        // Assert
        invoked.ShouldBeTrue();
        passedRegistry.ShouldBeSameAs(registry);
    }
}
