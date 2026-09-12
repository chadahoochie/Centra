using System.Collections.Concurrent;
using System.Reflection;
using Centra.State;

namespace Centra.ControlPlane.Tests.Unit.Common;

public sealed class FakeStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, (object Value, string ETag)> _data = new();
    private long _etagCounter;

    public ValueTask<StateEntry<T>?> GetAsync<T>(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{storeName}:{key}";
        if (_data.TryGetValue(fullKey, out var entry) && entry.Value is T typedValue)
        {
            return ValueTask.FromResult<StateEntry<T>?>(new StateEntry<T>(key, typedValue, entry.ETag));
        }

        return ValueTask.FromResult<StateEntry<T>?>(null);
    }

    public ValueTask SetAsync<T>(
        string storeName,
        string key,
        T value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{storeName}:{key}";
        var etag = Interlocked.Increment(ref _etagCounter).ToString();
        _data[fullKey] = (value!, etag);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySetAsync<T>(
        string storeName,
        string key,
        T value,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{storeName}:{key}";
        if (_data.TryGetValue(fullKey, out var existing))
        {
            if (expectedETag != "*" && existing.ETag != expectedETag)
            {
                return ValueTask.FromResult(false);
            }
        }
        else if (!string.IsNullOrEmpty(expectedETag) && expectedETag != "*")
        {
            return ValueTask.FromResult(false);
        }

        var newETag = Interlocked.Increment(ref _etagCounter).ToString();
        _data[fullKey] = (value!, newETag);
        return ValueTask.FromResult(true);
    }

    public ValueTask DeleteAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{storeName}:{key}";
        _data.TryRemove(fullKey, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryDeleteAsync(
        string storeName,
        string key,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{storeName}:{key}";
        if (_data.TryGetValue(fullKey, out var existing) && existing.ETag == expectedETag)
        {
            return ValueTask.FromResult(_data.TryRemove(fullKey, out _));
        }

        return ValueTask.FromResult(false);
    }

    public ValueTask ExecuteTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        foreach (var op in operations)
        {
            var fullKey = $"{storeName}:{op.Key}";
            if (op is DeleteTransactionOperation)
            {
                _data.TryRemove(fullKey, out _);
            }
            else
            {
                var valProp = op.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                if (valProp != null)
                {
                    var val = valProp.GetValue(op);
                    var etag = Interlocked.Increment(ref _etagCounter).ToString();
                    _data[fullKey] = (val!, etag);
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}
