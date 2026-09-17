namespace Centra.PubSub;

public readonly record struct PubSubQueueStats(long MessageCount, int ConsumerCount);
