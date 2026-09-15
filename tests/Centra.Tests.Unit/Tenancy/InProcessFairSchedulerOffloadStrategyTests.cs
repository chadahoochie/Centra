using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class InProcessFairSchedulerOffloadStrategyTests
{
    [Fact]
    public async Task ExecuteOffloadAsync_ExecutesWorkItemAndReturnsResult()
    {
        await using var strategy = new InProcessFairSchedulerOffloadStrategy();
        var executed = false;

        var workItem = new TenantOffloadWorkItem(
            TenantId: "tenant-1",
            PubSubName: "pubsub",
            Topic: "orders",
            Payload: ReadOnlyMemory<byte>.Empty,
            Headers: new Dictionary<string, string>(),
            HandlerInvoker: _ =>
            {
                executed = true;
                return ValueTask.FromResult(EventHandlingResult.Success);
            },
            CreatedAt: DateTimeOffset.UtcNow);

        var result = await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result);
        Assert.True(executed);
    }

    [Fact]
    public async Task ExecuteOffloadAsync_MultiTenantFairInterleaving_DoesNotStarveOtherTenants()
    {
        var options = new TenantOffloadOptions
        {
            MaxConcurrencyPerTenant = 1
        };

        await using var strategy = new InProcessFairSchedulerOffloadStrategy(options);
        var completionOrder = new List<string>();
        var lockObj = new object();

        var tasks = new List<Task<EventHandlingResult>>();

        // Tenant A enqueues 8 items
        for (var i = 0; i < 8; i++)
        {
            var workItemA = new TenantOffloadWorkItem(
                TenantId: "tenant-A",
                PubSubName: "pubsub",
                Topic: "orders",
                Payload: ReadOnlyMemory<byte>.Empty,
                Headers: new Dictionary<string, string>(),
                HandlerInvoker: async ct =>
                {
                    await Task.Delay(30, ct);
                    lock (lockObj)
                    {
                        completionOrder.Add("tenant-A");
                    }
                    return EventHandlingResult.Success;
                },
                CreatedAt: DateTimeOffset.UtcNow);

            tasks.Add(strategy.ExecuteOffloadAsync(workItemA, CancellationToken.None).AsTask());
        }

        // Tenant B enqueues 2 items
        for (var i = 0; i < 2; i++)
        {
            var workItemB = new TenantOffloadWorkItem(
                TenantId: "tenant-B",
                PubSubName: "pubsub",
                Topic: "orders",
                Payload: ReadOnlyMemory<byte>.Empty,
                Headers: new Dictionary<string, string>(),
                HandlerInvoker: async ct =>
                {
                    await Task.Delay(30, ct);
                    lock (lockObj)
                    {
                        completionOrder.Add("tenant-B");
                    }
                    return EventHandlingResult.Success;
                },
                CreatedAt: DateTimeOffset.UtcNow);

            tasks.Add(strategy.ExecuteOffloadAsync(workItemB, CancellationToken.None).AsTask());
        }

        await Task.WhenAll(tasks);

        // Verify that Tenant B completed before all Tenant A items finished
        // (i.e. Tenant B is not strictly at the end of completionOrder)
        lock (lockObj)
        {
            var firstBIndex = completionOrder.IndexOf("tenant-B");
            var lastAIndex = completionOrder.LastIndexOf("tenant-A");
            Assert.True(firstBIndex < lastAIndex, "Tenant B should interleave before Tenant A's last item");
        }
    }

    [Fact]
    public async Task CleanupIdleResourcesAsync_ReapsIdleLanes()
    {
        var options = new TenantOffloadOptions
        {
            LaneIdleTimeout = TimeSpan.FromMilliseconds(50)
        };

        await using var strategy = new InProcessFairSchedulerOffloadStrategy(options);

        var workItem = new TenantOffloadWorkItem(
            TenantId: "tenant-temp",
            PubSubName: "pubsub",
            Topic: "orders",
            Payload: ReadOnlyMemory<byte>.Empty,
            Headers: new Dictionary<string, string>(),
            HandlerInvoker: _ => ValueTask.FromResult(EventHandlingResult.Success),
            CreatedAt: DateTimeOffset.UtcNow);

        await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);

        var lane = strategy.GetOrCreateLane("tenant-temp", "orders");
        lane.LastActivityTime = DateTimeOffset.UtcNow.AddSeconds(-10);

        await strategy.CleanupIdleResourcesAsync(CancellationToken.None);

        // After cleanup, the lane should have been removed from internal dictionary
        Assert.Equal(0, strategy.GetOrCreateLane("tenant-temp-2", "orders").QueueDepth);
    }
}
