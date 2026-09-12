using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Tests.Unit.Common;
using Centra.Sync;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Resilience;

public sealed class StateStoreResiliencePolicyCatalogTests
{
    [Fact]
    public async Task Should_Upsert_And_Retrieve_Policy_With_Incremented_Revision()
    {
        // Arrange
        var stateStore = new FakeStateStore();
        var catalog = new StateStoreResiliencePolicyCatalog(stateStore);
        var dto = new ResiliencePolicyDto
        {
            PolicyName = "retry-fast",
            MaxRetries = 3,
            BackoffType = "Constant",
            BaseDelayMs = 100
        };

        // Act - Insert
        var entry1 = await catalog.UpsertPolicyAsync(dto);
        var fetched = await catalog.GetPolicyAsync("retry-fast");

        // Assert
        entry1.Revision.ShouldBe(1);
        entry1.Policy.PolicyName.ShouldBe("retry-fast");
        fetched.ShouldNotBeNull();
        fetched.Revision.ShouldBe(1);
        fetched.Policy.MaxRetries.ShouldBe(3);
        fetched.Policy.BackoffType.ShouldBe("Constant");

        // Act - Update
        var updatedDto = dto with
        {
            MaxRetries = 5,
            BackoffType = "Exponential"
        };
        var entry2 = await catalog.UpsertPolicyAsync(updatedDto);

        // Assert
        entry2.Revision.ShouldBe(2);
        entry2.Policy.MaxRetries.ShouldBe(5);

        var currentRevision = await catalog.GetCurrentRevisionAsync();
        currentRevision.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Retrieve_All_Policies()
    {
        // Arrange
        var stateStore = new FakeStateStore();
        var catalog = new StateStoreResiliencePolicyCatalog(stateStore);
        await catalog.UpsertPolicyAsync(new ResiliencePolicyDto
        {
            PolicyName = "p1",
            TimeoutSeconds = 5
        });
        await catalog.UpsertPolicyAsync(new ResiliencePolicyDto
        {
            PolicyName = "p2",
            TimeoutSeconds = 10
        });

        // Act
        var all = await catalog.GetAllPoliciesAsync();

        // Assert
        all.Count.ShouldBe(2);
        all.ShouldContain(p => p.Policy.PolicyName == "p1");
        all.ShouldContain(p => p.Policy.PolicyName == "p2");
    }

    [Fact]
    public async Task Should_Delete_Policy_And_Increment_Revision()
    {
        // Arrange
        var stateStore = new FakeStateStore();
        var catalog = new StateStoreResiliencePolicyCatalog(stateStore);
        await catalog.UpsertPolicyAsync(new ResiliencePolicyDto
        {
            PolicyName = "temp-policy",
            MaxRetries = 1
        });

        // Act
        var deleted = await catalog.DeletePolicyAsync("temp-policy");
        var fetched = await catalog.GetPolicyAsync("temp-policy");
        var currentRevision = await catalog.GetCurrentRevisionAsync();

        // Assert
        deleted.ShouldBeTrue();
        fetched.ShouldBeNull();
        currentRevision.ShouldBe(2);

        // Deleting non-existent policy returns false
        var deleteAgain = await catalog.DeletePolicyAsync("temp-policy");
        deleteAgain.ShouldBeFalse();
        (await catalog.GetCurrentRevisionAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Should_Persist_Across_Catalog_Reinstantiation()
    {
        // Arrange
        var stateStore = new FakeStateStore();
        var catalog1 = new StateStoreResiliencePolicyCatalog(stateStore);
        await catalog1.UpsertPolicyAsync(new ResiliencePolicyDto
        {
            PolicyName = "persistent-policy",
            MaxRetries = 4,
            BackoffType = "Exponential"
        });

        // Act - Reinstantiate catalog with same state store
        var catalog2 = new StateStoreResiliencePolicyCatalog(stateStore);
        var fetched = await catalog2.GetPolicyAsync("persistent-policy");
        var revision = await catalog2.GetCurrentRevisionAsync();

        // Assert
        fetched.ShouldNotBeNull();
        fetched.Revision.ShouldBe(1);
        fetched.Policy.MaxRetries.ShouldBe(4);
        revision.ShouldBe(1);
    }

    [Fact]
    public void Should_Throw_When_StateStore_Is_Null()
    {
        Should.Throw<ArgumentNullException>(() => new StateStoreResiliencePolicyCatalog(null!));
    }
}
