namespace Centra.PubSub;

/// <summary>
/// Represents an individual message within a pub/sub batch or publish operation.
/// </summary>
/// <param name="Payload">The raw binary payload of the event.</param>
/// <param name="Metadata">Optional CloudEvent headers and transport metadata.</param>
public readonly record struct PubSubMessage(
    ReadOnlyMemory<byte> Payload,
    IReadOnlyDictionary<string, string>? Metadata = null);
