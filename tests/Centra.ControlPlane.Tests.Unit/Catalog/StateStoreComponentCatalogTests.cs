using Centra.Components;
using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Tests.Unit.Common;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Catalog;

public sealed class StateStoreComponentCatalogTests
{
    [Fact]
    public async Task Should_Upsert_And_Retrieve_Component_With_Incremented_Revision()
    {
        // Arrange
        var stateStore = new FakeStateStore();
        var catalog = new StateStoreComponentCatalog(stateStore);
        var def = new ComponentDefinition
        {
            Name = "cache-store",
            Type = ComponentType.StateStore,
            Provider = "redis"
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
        var stateStore = new FakeStateStore();
        var catalog = new StateStoreComponentCatalog(stateStore);
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "s1", Type = ComponentType.StateStore, Provider = "redis" });
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "p1", Type = ComponentType.PubSub, Provider = "redis" });
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "s2", Type = ComponentType.StateStore, Provider = "redis" });

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
        var stateStore = new FakeStateStore();
        var catalog = new StateStoreComponentCatalog(stateStore);
        await catalog.UpsertComponentAsync(new ComponentDefinition { Name = "to-delete", Type = ComponentType.Binding, Provider = "cron" });

        // Act
        var deleted = await catalog.DeleteComponentAsync("to-delete");
        var fetched = await catalog.GetComponentAsync("to-delete");
        var currentRevision = await catalog.GetCurrentRevisionAsync();

        // Assert
        deleted.ShouldBeTrue();
        fetched.ShouldBeNull();
        currentRevision.ShouldBe(2);

        // Deleting non-existent should return false and not increment revision
        var deleteAgain = await catalog.DeleteComponentAsync("to-delete");
        deleteAgain.ShouldBeFalse();
        (await catalog.GetCurrentRevisionAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Should_Persist_Across_Catalog_Reinstantiation()
    {
        // Arrange
        var stateStore = new FakeStateStore();
        var catalog1 = new StateStoreComponentCatalog(stateStore);
        await catalog1.UpsertComponentAsync(new ComponentDefinition { Name = "statestore-1", Type = ComponentType.StateStore, Provider = "postgres" });
        await catalog1.UpsertComponentAsync(new ComponentDefinition { Name = "pubsub-1", Type = ComponentType.PubSub, Provider = "servicebus" });

        // Act - create new catalog instance pointing to same state store
        var catalog2 = new StateStoreComponentCatalog(stateStore);
        var components = await catalog2.GetAllComponentsAsync();
        var revision = await catalog2.GetCurrentRevisionAsync();
        var item = await catalog2.GetComponentAsync("statestore-1");

        // Assert
        components.Count.ShouldBe(2);
        revision.ShouldBe(2);
        item.ShouldNotBeNull();
        item.Definition.Provider.ShouldBe("postgres");
    }

    [Fact]
    public void Should_Throw_When_StateStore_Is_Null()
    {
        Should.Throw<ArgumentNullException>(() => new StateStoreComponentCatalog(null!));
    }
}
