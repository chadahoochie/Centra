namespace Centra.Sync;

public sealed class ControlPlaneEndpointSelector
{
    private readonly string[] _endpoints;
    private readonly Lock _lock = new();
    private int _currentIndex;

    public ControlPlaneEndpointSelector(IReadOnlyList<string> endpoints)
    {
        if (endpoints is null || endpoints.Count == 0)
        {
            throw new ArgumentException("At least one endpoint must be provided.", nameof(endpoints));
        }

        _endpoints = endpoints.ToArray();
        _currentIndex = 0;
    }

    public string GetCurrentEndpoint()
    {
        lock (_lock)
        {
            return _endpoints[_currentIndex];
        }
    }

    public void MarkEndpointFailed(string endpoint)
    {
        lock (_lock)
        {
            if (string.Equals(_endpoints[_currentIndex], endpoint, StringComparison.OrdinalIgnoreCase))
            {
                _currentIndex = (_currentIndex + 1) % _endpoints.Length;
            }
        }
    }

    public void SetCurrentLeader(string leaderEndpoint)
    {
        if (string.IsNullOrWhiteSpace(leaderEndpoint))
        {
            return;
        }

        lock (_lock)
        {
            for (int i = 0; i < _endpoints.Length; i++)
            {
                if (string.Equals(_endpoints[i], leaderEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    _currentIndex = i;
                    return;
                }
            }
        }
    }
}
