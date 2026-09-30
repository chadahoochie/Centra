namespace Centra.Providers.Flotilla.Client;

/// <summary>
/// Client contract for interacting with the Flotilla Raft consensus cluster.
/// </summary>
public interface IFlotillaClient : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Proposes a binary payload to the Flotilla cluster with sub-80µs quorum commitment.
    /// </summary>
    ValueTask<FlotillaProposalResult> ProposeAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to the stream of sequentially committed entries from the consensus log.
    /// </summary>
    IAsyncEnumerable<CommittedEntry> SubscribeCommitsAsync(
        CancellationToken cancellationToken = default);
}
