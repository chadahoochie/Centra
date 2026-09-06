using System.Collections.Concurrent;
using Centra.Components;

namespace Centra.ControlPlane.Secrets;

public sealed class InMemorySecretStore : IControlPlaneSecretResolver
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public void SetSecret(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _secrets[key] = value;
    }

    public string? GetSecret(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _secrets.TryGetValue(key, out var val);
        return val;
    }

    public ValueTask<ComponentDefinition> ResolveSecretsAsync(ComponentDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.SecretReferences is null || definition.SecretReferences.Count == 0)
        {
            return ValueTask.FromResult(definition);
        }

        var resolvedMetadata = new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var (targetMetaKey, secretKey) in definition.SecretReferences)
        {
            if (_secrets.TryGetValue(secretKey, out var secretValue))
            {
                resolvedMetadata[targetMetaKey] = secretValue;
            }
        }

        var resolvedDefinition = definition with
        {
            Metadata = resolvedMetadata
        };

        return ValueTask.FromResult(resolvedDefinition);
    }
}
