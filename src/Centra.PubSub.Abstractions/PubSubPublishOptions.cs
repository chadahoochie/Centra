using Centra.Events;

namespace Centra.PubSub;

public sealed record PubSubPublishOptions
{
    public CloudEventMode Mode { get; init; } = CloudEventMode.Binary;
    public string? ContentType { get; init; } = CloudEventConstants.DefaultContentType;
    public TimeSpan? TimeToLive { get; init; }
    public bool DisableResilience { get; init; } = false;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
