using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Centra.ControlPlane.Sync;

public sealed class ComponentSyncDispatcher : IComponentSyncDispatcher
{
    private readonly ConcurrentDictionary<string, Channel<ComponentSyncEvent>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public ValueTask PublishEventAsync(ComponentSyncEvent syncEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(syncEvent);

        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(syncEvent);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ComponentSyncEvent> SubscribeAsync(
        string appId,
        string instanceId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriberKey = $"{appId}:{instanceId}:{Guid.NewGuid():N}";
        var channel = Channel.CreateUnbounded<ComponentSyncEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

        _subscribers[subscriberKey] = channel;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ComponentSyncEvent evt;
                try
                {
                    evt = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                yield return evt;
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriberKey, out _);
            channel.Writer.TryComplete();
        }
    }
}
