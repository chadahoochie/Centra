using System.Text;
using Centra.Providers.PostgreSql.Locks;
using Centra.Providers.PostgreSql.Options;
using Centra.Providers.PostgreSql.State;
using Centra.State;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class PostgreSqlProviderIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.3")
        .Build();

    private NpgsqlDataSource? _dataSource;
    private PostgreSqlStateStoreDriver? _stateDriver;
    private PostgreSqlDistributedLockDriver? _lockDriver;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var connStr = _container.GetConnectionString();
        _dataSource = NpgsqlDataSource.Create(connStr);

        var options = Microsoft.Extensions.Options.Options.Create(new PostgreSqlProviderOptions
        {
            ConnectionString = connStr,
            SchemaName = "public",
            StateTableName = "centra_state_it",
            LockTableName = "centra_locks_it",
            AutoCreateTable = true
        });

        _stateDriver = new PostgreSqlStateStoreDriver(_dataSource, options);
        _lockDriver = new PostgreSqlDistributedLockDriver(_dataSource, options);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Should_Set_Get_TrySet_CAS_And_ExecuteTransaction_In_Real_PostgreSql()
    {
        _stateDriver.ShouldNotBeNull();
        const string store = "statestore";
        const string key = "order-pg-1";
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"order-pg-1\",\"status\":\"Pending\"}");

        // 1. Initial get is null
        var initial = await _stateDriver.GetAsync(store, key);
        initial.ShouldBeNull();

        // 2. Set state
        await _stateDriver.SetAsync(store, key, payload);

        // 3. Get state returns matching entry
        var saved = await _stateDriver.GetAsync(store, key);
        saved.ShouldNotBeNull();
        saved.Value.Key.ShouldBe(key);
        saved.Value.Value.ShouldBe(payload);
        saved.Value.ETag.ShouldNotBeNullOrWhiteSpace();

        var originalEtag = saved.Value.ETag;

        // 4. TrySet with stale ETag fails
        var updatedPayload = Encoding.UTF8.GetBytes("{\"id\":\"order-pg-1\",\"status\":\"Completed\"}");
        var conflict = await _stateDriver.TrySetAsync(store, key, updatedPayload, "stale-etag-xyz");
        conflict.ShouldBeFalse();

        // 5. TrySet with valid ETag succeeds
        var success = await _stateDriver.TrySetAsync(store, key, updatedPayload, originalEtag);
        success.ShouldBeTrue();

        var updated = await _stateDriver.GetAsync(store, key);
        updated.ShouldNotBeNull();
        updated.Value.Value.ShouldBe(updatedPayload);
        updated.Value.ETag.ShouldNotBe(originalEtag);

        // 6. Execute atomic transaction (batch set and delete)
        var ops = new List<StateTransactionOperation>
        {
            new SetTransactionOperation<byte[]>("tx-key-1", Encoding.UTF8.GetBytes("tx-data-1")),
            new SetTransactionOperation<byte[]>("tx-key-2", Encoding.UTF8.GetBytes("tx-data-2")),
            new DeleteTransactionOperation(key)
        };

        await _stateDriver.ExecuteTransactionAsync(store, ops);

        // Verify key was deleted by tx
        var deletedAfterTx = await _stateDriver.GetAsync(store, key);
        deletedAfterTx.ShouldBeNull();

        // Verify tx items exist
        var txItem1 = await _stateDriver.GetAsync(store, "tx-key-1");
        txItem1.ShouldNotBeNull();
        txItem1.Value.Value.ShouldBe(Encoding.UTF8.GetBytes("tx-data-1"));
    }

    [Fact]
    public async Task Should_Acquire_Lock_Prevent_Concurrency_And_Renew_Via_PostgreSql_Lock()
    {
        _lockDriver.ShouldNotBeNull();
        const string lockStore = "lockstore";
        const string resource = "resource-pg-lock-1";

        // 1. Acquire lock
        await using var lock1 = await _lockDriver.TryAcquireLockAsync(lockStore, resource, TimeSpan.FromSeconds(10));
        lock1.ShouldNotBeNull();
        lock1.ResourceId.ShouldBe(resource);

        // 2. Mutual exclusion: concurrent attempt fails
        var lock2 = await _lockDriver.TryAcquireLockAsync(lockStore, resource, TimeSpan.FromSeconds(10));
        lock2.ShouldBeNull();

        // 3. Renew lock
        var renewed = await lock1.RenewAsync(TimeSpan.FromSeconds(30));
        renewed.ShouldBeTrue();

        // 4. Release lock 1
        await lock1.DisposeAsync();

        // 5. Concurrent attempt now succeeds
        await using var lock3 = await _lockDriver.TryAcquireLockAsync(lockStore, resource, TimeSpan.FromSeconds(10));
        lock3.ShouldNotBeNull();
    }
}
