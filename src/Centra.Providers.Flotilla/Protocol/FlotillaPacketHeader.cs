using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Centra.Providers.Flotilla.Protocol;

/// <summary>
/// Fixed 40-byte binary header framing all Flotilla UDP datagrams.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct FlotillaPacketHeader
{
    public const uint ExpectedMagic = 0x464C5431; // 'FLT1'
    public const ushort CurrentVersion = 1;
    public const int HeaderSize = 40;

    public uint Magic { get; }
    public ushort Version { get; }
    public ushort MsgType { get; }
    public ulong SenderId { get; }
    public ulong ReceiverId { get; }
    public ulong Term { get; }
    public uint Checksum { get; }
    public uint PayloadLen { get; }

    public FlotillaPacketHeader(
        uint magic,
        ushort version,
        ushort msgType,
        ulong senderId,
        ulong receiverId,
        ulong term,
        uint checksum,
        uint payloadLen)
    {
        Magic = magic;
        Version = version;
        MsgType = msgType;
        SenderId = senderId;
        ReceiverId = receiverId;
        Term = term;
        Checksum = checksum;
        PayloadLen = payloadLen;
    }

    /// <summary>
    /// Writes this header into the target byte span in little-endian format.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException("Destination span is too small for header", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), MsgType);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), SenderId);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(16, 8), ReceiverId);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(24, 8), Term);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(32, 4), Checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(36, 4), PayloadLen);
    }

    /// <summary>
    /// Reads a header from the source byte span in little-endian format.
    /// </summary>
    public static FlotillaPacketHeader ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
        {
            throw new ArgumentException("Source span is too small for header", nameof(source));
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(source[..4]);
        var version = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2));
        var msgType = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2));
        var senderId = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(8, 8));
        var receiverId = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(16, 8));
        var term = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(24, 8));
        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(32, 4));
        var payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(36, 4));

        return new FlotillaPacketHeader(magic, version, msgType, senderId, receiverId, term, checksum, payloadLen);
    }
}
