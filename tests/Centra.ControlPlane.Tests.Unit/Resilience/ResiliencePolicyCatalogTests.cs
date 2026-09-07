using Centra.ControlPlane.Catalog;
using Centra.Sync;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Resilience;

public sealed class ResiliencePolicyCatalogTests
{
    [Fact]
    public async Task Should_Upsert_And_Retrieve_Policy_With_Incremented_Revision()
    {
        // Arrange
        var catalog = new InMemoryResiliencePolicyCatalog();
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
        var catalog = new InMemoryResiliencePolicyCatalog();
        await catalog.UpsertPolicyAsync(new ResiliencePolicyDto
        {
            PolicyName = "p1",
            TimeoutSeconds = 5
        });
        await catalog.UpsertPolicyAsync(new ResiliencePolicyDto
        {
            PolicyName = "p2",
            MaxRetries = 2
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
        var catalog = new InMemoryResiliencePolicyCatalog();
        await catalog.UpsertPolicyAsync(new ResiliencePolicyDto
        {
            PolicyName = "temp-policy",
            MaxRetries = 1
        });

        // Act
        var deleted = await catalog.DeletePolicyAsync("temp-policy");
        var secondDelete = await catalog.DeletePolicyAsync("temp-policy");
        var fetched = await catalog.GetPolicyAsync("temp-policy");

        // Assert
        deleted.ShouldBeTrue();
        secondDelete.ShouldBeFalse();
        fetched.ShouldBeNull();

        var currentRevision = await catalog.GetCurrentRevisionAsync();
        currentRevision.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Throw_On_Null_Policy_In_Upsert()
    {
        var catalog = new InMemoryResiliencePolicyCatalog();
        await Should.ThrowAsync<ArgumentNullException>(async () => await catalog.UpsertPolicyAsync(null!));
    }
}
