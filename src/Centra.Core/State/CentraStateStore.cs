using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Registry;
using Centra.Serialization;
using Centra.State;

namespace Centra.State;

public sealed class CentraStateStore : IStateStore
{
    private readonly ComponentRegistry _registry;
    private readonly ICentraSerializer _serializer;

    public CentraStateStore(ComponentRegistry registry, ICentraSerializer? serializer = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serializer = serializer ?? JsonCentraSerializer.Default;
    }

    public async ValueTask<StateEntry<T>?> GetAsync<T>(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var driver = GetDriver(storeName);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartStateActivity("Get", storeName, key);

        try
        {
            var rawEntry = await driver.GetAsync(storeName, key, options, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "Get", "success", durationMs);

            if (!rawEntry.HasValue)
            {
                return null;
            }

            var deserialized = _serializer.Deserialize<T>(rawEntry.Value.Value);
            if (deserialized is null)
            {
                return null;
            }

            return new StateEntry<T>(rawEntry.Value.Key, deserialized, rawEntry.Value.ETag, rawEntry.Value.Metadata);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "Get", "error", durationMs);
            throw;
        }
    }

    public async ValueTask SetAsync<T>(
        string storeName,
        string key,
        T value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var driver = GetDriver(storeName);
        var bytes = _serializer.Serialize(value);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartStateActivity("Set", storeName, key);

        try
        {
            await driver.SetAsync(storeName, key, bytes, options, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "Set", "success", durationMs);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "Set", "error", durationMs);
            throw;
        }
    }

    public async ValueTask<bool> TrySetAsync<T>(
        string storeName,
        string key,
        T value,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var driver = GetDriver(storeName);
        var bytes = _serializer.Serialize(value);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartStateActivity("TrySet", storeName, key);

        try
        {
            var success = await driver.TrySetAsync(storeName, key, bytes, expectedETag, options, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "TrySet", success ? "success" : "concurrency_conflict", durationMs);
            return success;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "TrySet", "error", durationMs);
            throw;
        }
    }

    public async ValueTask DeleteAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var driver = GetDriver(storeName);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartStateActivity("Delete", storeName, key);

        try
        {
            await driver.DeleteAsync(storeName, key, options, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "Delete", "success", durationMs);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "Delete", "error", durationMs);
            throw;
        }
    }

    public async ValueTask<bool> TryDeleteAsync(
        string storeName,
        string key,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var driver = GetDriver(storeName);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartStateActivity("TryDelete", storeName, key);

        try
        {
            var success = await driver.TryDeleteAsync(storeName, key, expectedETag, options, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "TryDelete", success ? "success" : "concurrency_conflict", durationMs);
            return success;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "TryDelete", "error", durationMs);
            throw;
        }
    }

    public async ValueTask ExecuteTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        var driver = GetDriver(storeName);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartStateActivity("ExecuteTransaction", storeName, "batch");

        try
        {
            await driver.ExecuteTransactionAsync(storeName, operations, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "ExecuteTransaction", "success", durationMs);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordStateOperation(storeName, "ExecuteTransaction", "error", durationMs);
            throw;
        }
    }

    private IStateStoreDriver GetDriver(string storeName)
    {
        var driver = _registry.GetStateStoreDriver(storeName);
        if (driver is null)
        {
            throw new InvalidOperationException($"No StateStore driver registered for store '{storeName}'");
        }

        return driver;
    }
}
