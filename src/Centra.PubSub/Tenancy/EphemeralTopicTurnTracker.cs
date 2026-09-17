using System.Collections.Concurrent;

namespace Centra.PubSub.Tenancy;

public sealed class EphemeralTopicTurnTracker
{
    private readonly ConcurrentDictionary<(string PubSubName, string Topic), int> _activeTurns = new();

    public void Enter((string PubSubName, string Topic) key)
    {
        _activeTurns.AddOrUpdate(key, 1, static (_, current) => current + 1);
    }

    public void Exit((string PubSubName, string Topic) key)
    {
        _activeTurns.AddOrUpdate(key, 0, static (_, current) => current > 0 ? current - 1 : 0);
    }

    public int GetActiveTurns((string PubSubName, string Topic) key)
    {
        return _activeTurns.TryGetValue(key, out var turns) ? Math.Max(0, turns) : 0;
    }

    public bool TryRemove((string PubSubName, string Topic) key)
    {
        return _activeTurns.TryRemove(key, out _);
    }
}
