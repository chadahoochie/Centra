using System.Text.Json;
using Centra.Providers.CosmosDb.Documents;
using Shouldly;
using Xunit;

namespace Centra.Providers.CosmosDb.Tests.Unit.Documents;

public sealed class CosmosDocumentTests
{
    [Fact]
    public void StateDocument_Should_Serialize_And_Deserialize_Correctly()
    {
        var now = DateTimeOffset.UtcNow;
        var doc = new CosmosStateDocument
        {
            Id = "item-1",
            StoreName = "orders",
            Key = "item-1",
            Value = Convert.ToBase64String([1, 2, 3, 4]),
            ETag = "\"etag-123\"",
            TimeToLive = 3600,
            UpdatedAtUtc = now
        };

        var json = JsonSerializer.Serialize(doc);
        json.ShouldContain("\"id\":\"item-1\"");
        json.ShouldContain("\"storeName\":\"orders\"");
        json.ShouldContain("\"ttl\":3600");

        var deserialized = JsonSerializer.Deserialize<CosmosStateDocument>(json);
        deserialized.ShouldNotBeNull();
        deserialized.Id.ShouldBe(doc.Id);
        deserialized.StoreName.ShouldBe(doc.StoreName);
        deserialized.Value.ShouldBe(doc.Value);
        deserialized.TimeToLive.ShouldBe(3600);
    }

    [Fact]
    public void LockDocument_Should_Serialize_And_Deserialize_Correctly()
    {
        var now = DateTimeOffset.UtcNow;
        var doc = new CosmosLockDocument
        {
            Id = "res-lock-1",
            LockStore = "locks",
            ResourceId = "res-lock-1",
            LockId = "token-abc",
            AcquiredAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(5),
            ETag = "\"etag-lock\"",
            TimeToLive = 300
        };

        var json = JsonSerializer.Serialize(doc);
        json.ShouldContain("\"id\":\"res-lock-1\"");
        json.ShouldContain("\"lockStore\":\"locks\"");
        json.ShouldContain("\"lockId\":\"token-abc\"");

        var deserialized = JsonSerializer.Deserialize<CosmosLockDocument>(json);
        deserialized.ShouldNotBeNull();
        deserialized.ResourceId.ShouldBe("res-lock-1");
        deserialized.LockId.ShouldBe("token-abc");
    }
}
