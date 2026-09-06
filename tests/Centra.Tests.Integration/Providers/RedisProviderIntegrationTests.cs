using System.Text;
using Centra.Drivers;
using Centra.Providers.Redis.Locks;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.PubSub;
using Centra.Providers.Redis.State;
using Centra.PubSub;
using Centra.State;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class RedisProviderIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine")
        .Build();

    private IConnectionMultiplexer? _multiplexer;
    private RedisStateStoreDriver? _stateDriver;
    private RedisPubSubDriver? _pubSubDriver;
    private RedisDistributedLockDriver? _lockDriver;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var connStr = _container.GetConnectionString();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(connStr);

        var options = Microsoft.Extensions.Options.Options.Create(new RedisProviderOptions
        {
            ConnectionString = connStr,
            KeyPrefix = "centra_it:"
        });

        _stateDriver = new RedisStateStoreDriver(_multiplexer, options);
        _pubSubDriver = new RedisPubSubDriver(_multiplexer, options);
        _lockDriver = new RedisDistributedLockDriver(_multiplexer, options);
    }

    public async Task DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Should_Set_Get_TrySet_And_Delete_State_In_Real_Redis()
    {
        _stateDriver.ShouldNotBeNull();
        const string store = "statestore";
        const string key = "order-101";
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"order-101\",\"amount\":42.50}");

        // 1. Initial Get should be null
        var initial = await _stateDriver.GetAsync(store, key);
        initial.ShouldBeNull();

        // 2. Set state
        await _stateDriver.SetAsync(store, key, payload);

        // 3. Get state should return entry with ETag
        var saved = await _stateDriver.GetAsync(store, key);
        saved.ShouldNotBeNull();
        saved.Value.Key.ShouldBe(key);
        saved.Value.Value.ShouldBe(payload);
        saved.Value.ETag.ShouldNotBeNullOrWhiteSpace();

        var originalEtag = saved.Value.ETag;

        // 4. TrySet with mismatched ETag should fail (optimistic concurrency conflict)
        var updatedPayload = Encoding.UTF8.GetBytes("{\"id\":\"order-101\",\"amount\":99.99}");
        var conflictResult = await _stateDriver.TrySetAsync(store, key, updatedPayload, "stale-etag-999");
        conflictResult.ShouldBeFalse();

        // 5. TrySet with matching ETag should succeed
        var casResult = await _stateDriver.TrySetAsync(store, key, updatedPayload, originalEtag);
        casResult.ShouldBeTrue();

        // 6. Verify updated value and new ETag
        var updated = await _stateDriver.GetAsync(store, key);
        updated.ShouldNotBeNull();
        updated.Value.Value.ShouldBe(updatedPayload);
        updated.Value.ETag.ShouldNotBe(originalEtag);

        // 7. TryDelete with mismatched ETag should fail
        var delConflict = await _stateDriver.TryDeleteAsync(store, key, originalEtag);
        delConflict.ShouldBeFalse();

        // 8. TryDelete with correct ETag should succeed
        var delResult = await _stateDriver.TryDeleteAsync(store, key, updated.Value.ETag);
        delResult.ShouldBeTrue();

        // 9. Verify deleted
        var afterDelete = await _stateDriver.GetAsync(store, key);
        afterDelete.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Publish_And_Receive_CloudEvents_Via_Redis_PubSub()
    {
        _pubSubDriver.ShouldNotBeNull();
        const string pubSub = "pubsub";
        const string topic = "orders.notifications";
        var messagePayload = Encoding.UTF8.GetBytes("{\"orderId\":\"ord-404\"}");
        var headers = new Dictionary<string, string>
        {
            ["ce-id"] = "evt-redis-1",
            ["ce-type"] = "orders.created",
            ["ce-source"] = "centra://orders",
            ["ce-specversion"] = "1.0",
            ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
        };

        var receivedTcs = new TaskCompletionSource<(byte[] Payload, IReadOnlyDictionary<string, string> Headers)>(TaskCreationOptions.RunContinuationsAsynchronously);

        await _pubSubDriver.SubscribeAsync(pubSub, topic, (p, h, ct) =>
        {
            receivedTcs.TrySetResult((p.ToArray(), h));
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        // Publish event
        await _pubSubDriver.PublishAsync(pubSub, topic, messagePayload, headers);

        // Await receipt
        var completedTask = await Task.WhenAny(receivedTcs.Task, Task.Delay(5000));
        completedTask.ShouldBe(receivedTcs.Task);

        var received = await receivedTcs.Task;
        received.Payload.ShouldBe(messagePayload);
        received.Headers["ce-id"].ShouldBe("evt-redis-1");
        received.Headers["ce-type"].ShouldBe("orders.created");
        received.Headers["traceparent"].ShouldBe("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
    }

    [Fact]
    public async Task Should_Acquire_Lock_Prevent_Concurrency_And_Renew_Via_Redis_Lock()
    {
        _lockDriver.ShouldNotBeNull();
        const string lockStore = "lockstore";
        const string resource = "resource-order-1";

        // 1. Acquire lock
        await using var lock1 = await _lockDriver.TryAcquireLockAsync(lockStore, resource, TimeSpan.FromSeconds(10));
        lock1.ShouldNotBeNull();
        lock1.ResourceId.ShouldBe(resource);

        // 2. Parallel acquisition attempt on same resource should return null (mutual exclusion)
        var lock2 = await _lockDriver.TryAcquireLockAsync(lockStore, resource, TimeSpan.FromSeconds(10));
        lock2.ShouldBeNull();

        // 3. Renew lock
        var renewed = await lock1.RenewAsync(TimeSpan.FromSeconds(20));
        renewed.ShouldBeTrue();

        // 4. Release lock 1
        await lock1.DisposeAsync();

        // 5. Now another acquisition should succeed
        await using var lock3 = await _lockDriver.TryAcquireLockAsync(lockStore, resource, TimeSpan.FromSeconds(10));
        lock3.ShouldNotBeNull();
    }
}
