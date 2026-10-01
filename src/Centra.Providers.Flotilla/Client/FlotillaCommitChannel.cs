using System.Threading.Channels;

namespace Centra.Providers.Flotilla.Client;

/// <summary>
/// Thread-safe commit channel coordinating ordered commit streams for Flotilla consensus clients.
/// </summary>
public sealed class FlotillaCommitChannel
{
    private readonly Channel<CommittedEntry> _channel;

    public FlotillaCommitChannel(int capacity = 10_000)
    {
        _channel = Channel.CreateBounded<CommittedEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false,
        });
    }

    /// <summary>
    /// Writes a committed log entry to the channel.
    /// </summary>
    public ValueTask WriteCommitAsync(CommittedEntry entry, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Reads all committed log entries as an asynchronous stream.
    /// </summary>
    public IAsyncEnumerable<CommittedEntry> ReadCommitsAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Completes the commit channel writer.
    /// </summary>
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }
}
