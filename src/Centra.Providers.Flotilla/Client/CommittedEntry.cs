namespace Centra.Providers.Flotilla.Client;

/// <summary>
/// A sequentially ordered log entry committed by the Flotilla Raft consensus cluster.
/// </summary>
public sealed class CommittedEntry
{
    public ulong LogIndex { get; init; }
    public ulong Term { get; init; }
    public ReadOnlyMemory<byte> Data { get; init; }
}
