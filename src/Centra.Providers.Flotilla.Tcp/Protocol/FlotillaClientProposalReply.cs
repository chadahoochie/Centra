using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Centra.Providers.Flotilla.Tcp.Protocol;

/// <summary>
/// Fixed 32-byte binary response frame for client proposal RPCs over TCP matching Flotilla protocol.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct FlotillaClientProposalReply
{
    public const int ReplySize = 32;

    public byte Success { get; }
    public ulong Index { get; }
    public ulong Term { get; }
    public ulong LeaderId { get; }

    public bool IsSuccess => Success == 1;

    public FlotillaClientProposalReply(byte success, ulong index, ulong term, ulong leaderId)
    {
        Success = success;
        Index = index;
        Term = term;
        LeaderId = leaderId;
    }

    /// <summary>
    /// Writes this reply into the destination byte span in little-endian format.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < ReplySize)
        {
            throw new ArgumentException("Destination span is too small for reply", nameof(destination));
        }

        destination[0] = Success;
        destination.Slice(1, 7).Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), Index);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(16, 8), Term);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(24, 8), LeaderId);
    }

    /// <summary>
    /// Reads a reply from the source byte span in little-endian format.
    /// </summary>
    public static FlotillaClientProposalReply ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < ReplySize)
        {
            throw new ArgumentException("Source span is too small for reply", nameof(source));
        }

        var success = source[0];
        var index = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(8, 8));
        var term = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(16, 8));
        var leaderId = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(24, 8));

        return new FlotillaClientProposalReply(success, index, term, leaderId);
    }
}
