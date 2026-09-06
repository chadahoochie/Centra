namespace Centra.Events;

public sealed record CloudEvent<T>
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public string SpecVersion { get; init; } = CloudEventConstants.SpecVersion10;
    public required string Type { get; init; }
    public string? DataContentType { get; init; } = CloudEventConstants.DefaultContentType;
    public string? DataSchema { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset? Time { get; init; }
    public T? Data { get; init; }

    // Enterprise Extensions
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? TenantId { get; init; }
    public string? SchemaVersion { get; init; }

    // Additional Extension Attributes
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}
