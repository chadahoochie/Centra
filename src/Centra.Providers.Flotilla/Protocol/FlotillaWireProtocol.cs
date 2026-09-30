using System.Text;

namespace Centra.Providers.Flotilla.Protocol;

/// <summary>
/// Binary wire protocol serialization for Centra messages over Flotilla Raft consensus.
/// Encodes topic, CloudEvent headers, and payload into a compact framed representation.
/// </summary>
public static class FlotillaWireProtocol
{
    /// <summary>
    /// ASCII magic bytes 'FLOT' (0x464C4F54).
    /// </summary>
    public const uint Magic = 0x464C4F54;

    /// <summary>
    /// Encodes a message with topic, metadata headers, and binary payload into a framed byte array.
    /// </summary>
    public static byte[] EncodeMessage(
        string topic,
        IReadOnlyDictionary<string, string>? metadata,
        ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(Magic);
        writer.Write(topic);

        if (metadata is not null && metadata.Count > 0)
        {
            writer.Write(metadata.Count);
            foreach (var (k, v) in metadata)
            {
                writer.Write(k);
                writer.Write(v ?? string.Empty);
            }
        }
        else
        {
            writer.Write(0);
        }

        writer.Write(payload.Length);
        writer.Write(payload);

        return ms.ToArray();
    }

    /// <summary>
    /// Decodes a framed message into its topic, metadata headers, and binary payload.
    /// </summary>
    public static (string Topic, Dictionary<string, string> Metadata, ReadOnlyMemory<byte> Payload) DecodeMessage(
        ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        var magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidDataException($"Invalid Flotilla message magic bytes: 0x{magic:X8}, expected 0x{Magic:X8}");
        }

        var topic = reader.ReadString();
        var metaCount = reader.ReadInt32();
        var metadata = new Dictionary<string, string>(metaCount);

        for (int i = 0; i < metaCount; i++)
        {
            var k = reader.ReadString();
            var v = reader.ReadString();
            metadata[k] = v;
        }

        var payloadLen = reader.ReadInt32();
        var payloadBytes = reader.ReadBytes(payloadLen);

        return (topic, metadata, payloadBytes);
    }
}
