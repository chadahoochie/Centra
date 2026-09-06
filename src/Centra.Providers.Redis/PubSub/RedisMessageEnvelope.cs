namespace Centra.Providers.Redis.PubSub;

public sealed record RedisMessageEnvelope(
    IReadOnlyDictionary<string, string>? Headers,
    byte[]? Payload);
