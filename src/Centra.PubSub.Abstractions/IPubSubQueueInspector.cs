namespace Centra.PubSub;

public interface IPubSubQueueInspector
{
    ValueTask<PubSubQueueStats?> GetQueueStatsAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default);
}
