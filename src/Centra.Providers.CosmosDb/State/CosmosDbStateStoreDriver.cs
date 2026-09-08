using System.Net;
using Centra.Drivers;
using Centra.Providers.CosmosDb.Documents;
using Centra.Providers.CosmosDb.Options;
using Centra.State;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Centra.Providers.CosmosDb.State;

public sealed class CosmosDbStateStoreDriver : IStateStoreDriver
{
    private readonly CosmosClient _client;
    private readonly CosmosDbProviderOptions _options;
    private int _initialized;

    public CosmosDbStateStoreDriver(
        CosmosClient client,
        IOptions<CosmosDbProviderOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? new CosmosDbProviderOptions();
    }

    private Container StateContainer => _client.GetContainer(_options.DatabaseName, _options.StateContainerName);

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_options.AutoCreateDatabaseAndContainers && Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            var dbResponse = await _client.CreateDatabaseIfNotExistsAsync(_options.DatabaseName, cancellationToken: cancellationToken).ConfigureAwait(false);
            var containerProperties = new ContainerProperties(_options.StateContainerName, _options.StatePartitionKeyPath)
            {
                DefaultTimeToLive = -1
            };
            await dbResponse.Database.CreateContainerIfNotExistsAsync(containerProperties, _options.Throughput, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<StateEntry<byte[]>?> GetAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await StateContainer.ReadItemAsync<CosmosStateDocument>(
                key,
                new PartitionKey(storeName),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var doc = response.Resource;
            if (doc is null)
            {
                return null;
            }

            var bytes = Convert.FromBase64String(doc.Value);
            var etag = response.ETag ?? doc.ETag ?? string.Empty;
            return new StateEntry<byte[]>(key, bytes, etag, null);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async ValueTask SetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        int? ttl = options?.TimeToLive.HasValue == true
            ? (int)Math.Max(1, options.TimeToLive.Value.TotalSeconds)
            : null;

        var doc = new CosmosStateDocument
        {
            Id = key,
            StoreName = storeName,
            Key = key,
            Value = Convert.ToBase64String(value.Span),
            TimeToLive = ttl,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await StateContainer.UpsertItemAsync(
            doc,
            new PartitionKey(storeName),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TrySetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        int? ttl = options?.TimeToLive.HasValue == true
            ? (int)Math.Max(1, options.TimeToLive.Value.TotalSeconds)
            : null;

        var doc = new CosmosStateDocument
        {
            Id = key,
            StoreName = storeName,
            Key = key,
            Value = Convert.ToBase64String(value.Span),
            TimeToLive = ttl,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        var requestOptions = new ItemRequestOptions
        {
            IfMatchEtag = expectedETag
        };

        try
        {
            await StateContainer.ReplaceItemAsync(
                doc,
                key,
                new PartitionKey(storeName),
                requestOptions,
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed || ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async ValueTask DeleteAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await StateContainer.DeleteItemAsync<CosmosStateDocument>(
                key,
                new PartitionKey(storeName),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Idempotent delete
            System.Diagnostics.Debug.WriteLine($"[CosmosDbStateStore] Key '{key}' not found during delete (idempotent): {ex.Message}");
        }
    }

    public async ValueTask<bool> TryDeleteAsync(
        string storeName,
        string key,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var requestOptions = new ItemRequestOptions
        {
            IfMatchEtag = expectedETag
        };

        try
        {
            await StateContainer.DeleteItemAsync<CosmosStateDocument>(
                key,
                new PartitionKey(storeName),
                requestOptions,
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed || ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async ValueTask ExecuteTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var batch = StateContainer.CreateTransactionalBatch(new PartitionKey(storeName));

        var now = DateTimeOffset.UtcNow;
        foreach (var op in operations)
        {
            switch (op)
            {
                case SetTransactionOperation<byte[]> setOp:
                    int? ttl = setOp.Options?.TimeToLive.HasValue == true
                        ? (int)Math.Max(1, setOp.Options.TimeToLive.Value.TotalSeconds)
                        : null;

                    var doc = new CosmosStateDocument
                    {
                        Id = setOp.Key,
                        StoreName = storeName,
                        Key = setOp.Key,
                        Value = Convert.ToBase64String(setOp.Value),
                        TimeToLive = ttl,
                        UpdatedAtUtc = now
                    };
                    batch.UpsertItem(doc);
                    break;

                case DeleteTransactionOperation delOp:
                    batch.DeleteItem(delOp.Key);
                    break;
            }
        }

        var response = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cosmos DB transactional batch failed: {response.StatusCode} - {response.ErrorMessage}");
        }
    }
}
