using System.Collections.Concurrent;
using Centra.Sync;

namespace Centra.ControlPlane.Catalog;

/// <summary>
/// In-memory thread-safe implementation of <see cref="IResiliencePolicyCatalog"/> with revision tracking.
/// </summary>
public sealed class InMemoryResiliencePolicyCatalog : IResiliencePolicyCatalog
{
    private readonly ConcurrentDictionary<string, ResiliencePolicyCatalogEntry> _policies = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private long _currentRevision;

    public InMemoryResiliencePolicyCatalog(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<ResiliencePolicyCatalogEntry?> GetPolicyAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _policies.TryGetValue(name, out var entry);
        return ValueTask.FromResult(entry);
    }

    public ValueTask<IReadOnlyCollection<ResiliencePolicyCatalogEntry>> GetAllPoliciesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ResiliencePolicyCatalogEntry> list = _policies.Values.ToArray();
        return ValueTask.FromResult(list);
    }

    public ValueTask<ResiliencePolicyCatalogEntry> UpsertPolicyAsync(ResiliencePolicyDto policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.PolicyName);

        var revision = Interlocked.Increment(ref _currentRevision);
        var entry = new ResiliencePolicyCatalogEntry(policy, revision, _timeProvider.GetUtcNow());
        _policies[policy.PolicyName] = entry;

        return ValueTask.FromResult(entry);
    }

    public ValueTask<bool> DeletePolicyAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var removed = _policies.TryRemove(name, out _);
        if (removed)
        {
            Interlocked.Increment(ref _currentRevision);
        }

        return ValueTask.FromResult(removed);
    }

    public ValueTask<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Interlocked.Read(ref _currentRevision));
    }
}
