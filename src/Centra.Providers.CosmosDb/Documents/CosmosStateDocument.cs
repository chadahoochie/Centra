using System.Text.Json.Serialization;

namespace Centra.Providers.CosmosDb.Documents;

public sealed class CosmosStateDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("storeName")]
    public string StoreName { get; set; } = "";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeToLive { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
