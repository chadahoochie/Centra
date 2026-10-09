using System.Collections.Concurrent;

namespace Centra.ControlPlane.Security;

public sealed class ClusterAdmissionNonceCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);

    public bool TryAddNonce(string nonce, DateTimeOffset now, TimeSpan window)
    {
        var cutoff = now.Subtract(window);
        foreach (var (key, timestamp) in _nonces)
        {
            if (timestamp < cutoff)
            {
                _nonces.TryRemove(key, out _);
            }
        }

        return _nonces.TryAdd(nonce, now);
    }
}
