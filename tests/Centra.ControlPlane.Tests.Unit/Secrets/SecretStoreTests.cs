using Centra.Components;
using Centra.ControlPlane.Secrets;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Secrets;

public sealed class SecretStoreTests
{
    [Fact]
    public async Task Should_Resolve_SecretReferences_Into_Metadata()
    {
        // Arrange
        var secretStore = new InMemorySecretStore();
        secretStore.SetSecret("redis-pw", "super-secret-password-123");

        var def = new ComponentDefinition
        {
            Name = "redis-state",
            Type = ComponentType.StateStore,
            Provider = "redis",
            Metadata = new Dictionary<string, string>
            {
                ["host"] = "localhost:6379"
            },
            SecretReferences = new Dictionary<string, string>
            {
                ["password"] = "redis-pw"
            }
        };

        // Act
        var resolved = await secretStore.ResolveSecretsAsync(def);

        // Assert
        resolved.Metadata["host"].ShouldBe("localhost:6379");
        resolved.Metadata["password"].ShouldBe("super-secret-password-123");
    }

    [Fact]
    public async Task Should_Return_Original_Definition_When_No_SecretReferences()
    {
        // Arrange
        var secretStore = new InMemorySecretStore();
        var def = new ComponentDefinition
        {
            Name = "simple-store",
            Type = ComponentType.StateStore,
            Provider = "in-memory"
        };

        // Act
        var resolved = await secretStore.ResolveSecretsAsync(def);

        // Assert
        resolved.ShouldBe(def);
    }
}
