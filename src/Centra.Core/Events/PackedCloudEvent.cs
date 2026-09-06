using Centra.Events;

namespace Centra.Events;

public readonly record struct PackedCloudEvent(
    ReadOnlyMemory<byte> Payload,
    IReadOnlyDictionary<string, string> Headers,
    CloudEventMode Mode);
