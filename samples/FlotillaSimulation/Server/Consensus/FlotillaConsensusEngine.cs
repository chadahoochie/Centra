using System.Collections.Concurrent;
using System.Threading.Channels;
using Centra.Providers.Flotilla.Client;

namespace Centra.Sample.FlotillaSimulation.Server.Consensus;

/// <summary>
/// In-process Raft consensus state machine coordinating monotonic log indices and commit broadcasting.
/// </summary>
public sealed class FlotillaConsensusEngine
{
    private readonly ConcurrentDictionary<Guid, Channel<CommittedEntry>> _subscribers = new();
    private ulong _currentTerm = 1;
    private ulong _commitIndex;
    private ulong _leaderId = 1;

    public ulong CurrentCommitIndex => Interlocked.Read(ref _commitIndex);
    public ulong CurrentTerm => Interlocked.Read(ref _currentTerm);
    public ulong LeaderId => Interlocked.Read(ref _leaderId);
    public int SubscriberCount => _subscribers.Count;

    public (bool Success, ulong LogIndex, ulong Term, ulong LeaderId) Propose(ReadOnlySpan<byte> payload)
    {
        var newIndex = Interlocked.Increment(ref _commitIndex);
        var term = CurrentTerm;
        var leader = LeaderId;

        var committed = new CommittedEntry
        {
            LogIndex = newIndex,
            Term = term,
            Data = payload.ToArray()
        };

        BroadcastCommit(committed);

        return (true, newIndex, term, leader);
    }

    internal void BroadcastCommit(CommittedEntry entry)
    {
        foreach (var (id, channel) in _subscribers)
        {
            if (!channel.Writer.TryWrite(entry))
            {
                // Channel full or completed
            }
        }
    }

    public async IAsyncEnumerable<CommittedEntry> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subId = Guid.NewGuid();
        var channel = Channel.CreateBounded<CommittedEntry>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

        _subscribers.TryAdd(subId, channel);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }
        finally
        {
            _subscribers.TryRemove(subId, out _);
        }
    }
}
