using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Registry;
using Centra.Resilience;
using Centra.Serialization;
using Centra.State;

namespace Centra.State;

public sealed class CentraStateStore : IStateStore
{
    private readonly ComponentRegistry _registry;
    private readonly ICentraSerializer _serializer;
    private readonly IResiliencePipelineProvider? _resilienceProvider;

    public CentraStateStore(
        ComponentRegistry registry,
        ICentraSerializer? serializer = null,
        IResiliencePipelineProvider? resilienceProvider = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serializer = serializer ?? JsonCentraSerializer.Default;
        _resilienceProvider = resilienceProvider;
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
            StateEntry<byte[]>? rawEntry;
            if (_resilienceProvider is not null && options?.DisableResilience != true)
            {
                var pipeline = _resilienceProvider.GetStateStorePipeline(storeName);
                rawEntry = await pipeline.ExecuteAsync(async ct => await driver.GetAsync(storeName, key, options, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                rawEntry = await driver.GetAsync(storeName, key, options, cancellationToken).ConfigureAwait(false);
            }

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
            if (_resilienceProvider is not null && options?.DisableResilience != true)
            {
                var pipeline = _resilienceProvider.GetStateStorePipeline(storeName);
                await pipeline.ExecuteAsync(async ct => await driver.SetAsync(storeName, key, bytes, options, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await driver.SetAsync(storeName, key, bytes, options, cancellationToken).ConfigureAwait(false);
            }

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
            bool success;
            if (_resilienceProvider is not null && options?.DisableResilience != true)
            {
                var pipeline = _resilienceProvider.GetStateStorePipeline(storeName);
                success = await pipeline.ExecuteAsync(async ct => await driver.TrySetAsync(storeName, key, bytes, expectedETag, options, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                success = await driver.TrySetAsync(storeName, key, bytes, expectedETag, options, cancellationToken).ConfigureAwait(false);
            }

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
            if (_resilienceProvider is not null && options?.DisableResilience != true)
            {
                var pipeline = _resilienceProvider.GetStateStorePipeline(storeName);
                await pipeline.ExecuteAsync(async ct => await driver.DeleteAsync(storeName, key, options, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await driver.DeleteAsync(storeName, key, options, cancellationToken).ConfigureAwait(false);
            }

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
            bool success;
            if (_resilienceProvider is not null && options?.DisableResilience != true)
            {
                var pipeline = _resilienceProvider.GetStateStorePipeline(storeName);
                success = await pipeline.ExecuteAsync(async ct => await driver.TryDeleteAsync(storeName, key, expectedETag, options, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                success = await driver.TryDeleteAsync(storeName, key, expectedETag, options, cancellationToken).ConfigureAwait(false);
            }

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
            var normalizedOps = NormalizeOperations(operations);
            if (_resilienceProvider is not null)
            {
                var pipeline = _resilienceProvider.GetStateStorePipeline(storeName);
                await pipeline.ExecuteAsync(async ct => await driver.ExecuteTransactionAsync(storeName, normalizedOps, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await driver.ExecuteTransactionAsync(storeName, normalizedOps, cancellationToken).ConfigureAwait(false);
            }

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

    private IReadOnlyList<StateTransactionOperation> NormalizeOperations(IReadOnlyList<StateTransactionOperation> operations)
    {
        if (operations.Count == 0)
        {
            return operations;
        }

        List<StateTransactionOperation>? converted = null;
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op is SetTransactionOperation<byte[]> || op is DeleteTransactionOperation)
            {
                converted?.Add(op);
                continue;
            }

            converted ??= new List<StateTransactionOperation>(operations.Take(i));
            converted.Add(NormalizeOperation(op));
        }

        return converted ?? operations;
    }

    private StateTransactionOperation NormalizeOperation(StateTransactionOperation op)
    {
        var opType = op.GetType();
        if (!opType.IsGenericType || opType.GetGenericTypeDefinition() != typeof(SetTransactionOperation<>))
        {
            return op;
        }

        var genericArg = opType.GetGenericArguments()[0];
        var valueProp = opType.GetProperty(nameof(SetTransactionOperation<object>.Value))!;
        var expectedETagProp = opType.GetProperty(nameof(SetTransactionOperation<object>.ExpectedETag))!;
        var optionsProp = opType.GetProperty(nameof(SetTransactionOperation<object>.Options))!;

        var rawValue = valueProp.GetValue(op);
        var expectedETag = (string?)expectedETagProp.GetValue(op);
        var options = (StateOptions?)optionsProp.GetValue(op);

        var bytes = rawValue switch
        {
            null => [],
            ReadOnlyMemory<byte> rom => rom.ToArray(),
            _ => _serializer.Serialize(rawValue, genericArg)
        };

        return new SetTransactionOperation<byte[]>(op.Key, bytes, expectedETag, options);
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
