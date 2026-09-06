using System.Text.Json.Serialization;

namespace Centra.Providers.CosmosDb.Documents;

public sealed class CosmosLockDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("lockStore")]
    public string LockStore { get; set; } = "";

    [JsonPropertyName("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonPropertyName("lockId")]
    public string LockId { get; set; } = "";

    [JsonPropertyName("acquiredAtUtc")]
    public DateTimeOffset AcquiredAtUtc { get; set; }

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset ExpiresAtUtc { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeToLive { get; set; }
}
