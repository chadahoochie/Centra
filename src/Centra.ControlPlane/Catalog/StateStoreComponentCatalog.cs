using Centra.Components;
using Centra.State;

namespace Centra.ControlPlane.Catalog;

/// <summary>
/// Persistent implementation of <see cref="IComponentCatalog"/> backed by any configured <see cref="IStateStore"/>.
/// </summary>
public sealed class StateStoreComponentCatalog : IComponentCatalog
{
    private const string IndexKey = "centra:controlplane:components:index";
    private const string RevisionKey = "centra:controlplane:components:revision";
    private readonly IStateStore _stateStore;
    private readonly string _storeName;
    private readonly TimeProvider _timeProvider;

    public StateStoreComponentCatalog(
        IStateStore stateStore,
        string storeName = "default",
        TimeProvider? timeProvider = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _storeName = storeName;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<ComponentCatalogEntry?> GetComponentAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var key = $"centra:controlplane:components:{name.ToLowerInvariant()}";
        var entry = await _stateStore.GetAsync<ComponentCatalogEntry>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.HasValue ? entry.Value.Value : null;
    }

    public async ValueTask<IReadOnlyCollection<ComponentCatalogEntry>> GetAllComponentsAsync(CancellationToken cancellationToken = default)
    {
        var indexEntry = await _stateStore.GetAsync<List<string>>(_storeName, IndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!indexEntry.HasValue || indexEntry.Value.Value.Count == 0)
        {
            return Array.Empty<ComponentCatalogEntry>();
        }

        var result = new List<ComponentCatalogEntry>();
        foreach (var name in indexEntry.Value.Value)
        {
            var component = await GetComponentAsync(name, cancellationToken).ConfigureAwait(false);
            if (component is not null)
            {
                result.Add(component);
            }
        }

        return result;
    }

    public async ValueTask<IReadOnlyCollection<ComponentCatalogEntry>> GetComponentsByTypeAsync(ComponentType type, CancellationToken cancellationToken = default)
    {
        var all = await GetAllComponentsAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(c => c.Definition.Type == type).ToArray();
    }

    public async ValueTask<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default)
    {
        var entry = await _stateStore.GetAsync<long>(_storeName, RevisionKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.HasValue ? entry.Value.Value : 0;
    }

    public async ValueTask<ComponentCatalogEntry> UpsertComponentAsync(ComponentDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var revision = await StateStoreRevisionCounter.IncrementRevisionAsync(_stateStore, _storeName, RevisionKey, _timeProvider, cancellationToken).ConfigureAwait(false);
        var entry = new ComponentCatalogEntry(definition, revision, _timeProvider.GetUtcNow());

        var key = $"centra:controlplane:components:{definition.Name.ToLowerInvariant()}";
        await _stateStore.SetAsync(_storeName, key, entry, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Update index
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var indexEntry = await _stateStore.GetAsync<List<string>>(_storeName, IndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            var list = indexEntry.HasValue ? new List<string>(indexEntry.Value.Value) : [];
            var lowerName = definition.Name.ToLowerInvariant();
            if (!list.Contains(lowerName))
            {
                list.Add(lowerName);
            }

            var etag = indexEntry?.ETag ?? string.Empty;
            var success = await _stateStore.TrySetAsync(_storeName, IndexKey, list, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                break;
            }
        }

        return entry;
    }

    public async ValueTask<bool> DeleteComponentAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var key = $"centra:controlplane:components:{name.ToLowerInvariant()}";
        var existing = await _stateStore.GetAsync<ComponentCatalogEntry>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!existing.HasValue)
        {
            return false;
        }

        await _stateStore.DeleteAsync(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        await StateStoreRevisionCounter.IncrementRevisionAsync(_stateStore, _storeName, RevisionKey, _timeProvider, cancellationToken).ConfigureAwait(false);

        // Update index
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var indexEntry = await _stateStore.GetAsync<List<string>>(_storeName, IndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!indexEntry.HasValue)
            {
                break;
            }

            var list = new List<string>(indexEntry.Value.Value);
            if (!list.Remove(name.ToLowerInvariant()))
            {
                break;
            }

            var etag = indexEntry.Value.ETag;
            var success = await _stateStore.TrySetAsync(_storeName, IndexKey, list, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                break;
            }
        }

        return true;
    }
}
