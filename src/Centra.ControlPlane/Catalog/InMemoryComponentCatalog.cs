using System.Collections.Concurrent;
using Centra.Components;

namespace Centra.ControlPlane.Catalog;

public sealed class InMemoryComponentCatalog : IComponentCatalog
{
    private readonly ConcurrentDictionary<string, ComponentCatalogEntry> _catalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private long _revisionCounter;

    public InMemoryComponentCatalog(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<ComponentCatalogEntry?> GetComponentAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _catalog.TryGetValue(name, out var entry);
        return ValueTask.FromResult(entry);
    }

    public ValueTask<IReadOnlyCollection<ComponentCatalogEntry>> GetAllComponentsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ComponentCatalogEntry> entries = _catalog.Values.ToArray();
        return ValueTask.FromResult(entries);
    }

    public ValueTask<IReadOnlyCollection<ComponentCatalogEntry>> GetComponentsByTypeAsync(ComponentType type, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ComponentCatalogEntry> entries = _catalog.Values.Where(e => e.Definition.Type == type).ToArray();
        return ValueTask.FromResult(entries);
    }

    public ValueTask<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Interlocked.Read(ref _revisionCounter));
    }

    public ValueTask<ComponentCatalogEntry> UpsertComponentAsync(ComponentDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var newRevision = Interlocked.Increment(ref _revisionCounter);
        var entry = new ComponentCatalogEntry(definition, newRevision, _timeProvider.GetUtcNow());
        _catalog[definition.Name] = entry;

        return ValueTask.FromResult(entry);
    }

    public ValueTask<bool> DeleteComponentAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var removed = _catalog.TryRemove(name, out _);
        if (removed)
        {
            Interlocked.Increment(ref _revisionCounter);
        }
        return ValueTask.FromResult(removed);
    }
}
