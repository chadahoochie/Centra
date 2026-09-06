using Centra.Components;
using Centra.ControlPlane.Catalog;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Catalog;

public sealed class ComponentCatalogTests
{
    [Fact]
    public async Task Should_Upsert_And_Retrieve_Component_With_Incremented_Revision()
    {
        // Arrange
        var catalog = new InMemoryComponentCatalog();
        var def = new ComponentDefinition
        {
            Name = "cache-store",
            Type = ComponentType.StateStore,
            Provider = "in-memory"
        };

        // Act
        var entry1 = await catalog.UpsertComponentAsync(def);
        var fetched = await catalog.GetComponentAsync("cache-store");

        // Assert
        entry1.Revision.ShouldBe(1);
        fetched.ShouldNotBeNull();
        fetched.Definition.Name.ShouldBe("cache-store");
        fetched.Revision.ShouldBe(1);

        // Update
        var updatedDef = def with { Version = "v2" };
        var entry2 = await catalog.UpsertComponentAsync(updatedDef);
        entry2.Revision.ShouldBe(2);

        var currentRevision = await catalog.GetCurrentRevisionAsync();
        currentRevision.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Filter_Components_By_Type()
    {
        // Arrange
        var catalog = new InMemoryComponentCatalog();
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "s1", Type = ComponentType.StateStore, Provider = "in-memory" });
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "p1", Type = ComponentType.PubSub, Provider = "in-memory" });
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "s2", Type = ComponentType.StateStore, Provider = "in-memory" });

        // Act
        var stateStores = await catalog.GetComponentsByTypeAsync(ComponentType.StateStore);
        var pubSubs = await catalog.GetComponentsByTypeAsync(ComponentType.PubSub);

        // Assert
        stateStores.Count.ShouldBe(2);
        pubSubs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Delete_Component_And_Increment_Revision()
    {
        // Arrange
        var catalog = new InMemoryComponentCatalog();
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "to-delete", Type = ComponentType.DistributedLock, Provider = "in-memory" });

        // Act
        var deleted = await catalog.DeleteComponentAsync("to-delete");
        var fetched = await catalog.GetComponentAsync("to-delete");
        var revision = await catalog.GetCurrentRevisionAsync();

        // Assert
        deleted.ShouldBeTrue();
        fetched.ShouldBeNull();
        revision.ShouldBe(2);
    }
}
