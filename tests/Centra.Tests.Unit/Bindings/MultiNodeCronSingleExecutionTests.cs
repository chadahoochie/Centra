using System.Collections.Concurrent;
using Centra.Bindings;
using Centra.Locks;
using Centra.Providers.InMemory.Locks;
using Centra.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class MultiNodeCronSingleExecutionTests : IDisposable
{
    private readonly InMemoryDistributedLockDriver _sharedLockDriver;
    private readonly CentraDistributedLockProvider _lockProvider;
    private readonly List<CentraCronScheduler> _schedulers = new();

    public MultiNodeCronSingleExecutionTests()
    {
        _sharedLockDriver = new InMemoryDistributedLockDriver();
        var registry = new ComponentRegistry();
        registry.RegisterLockDriver("lockstore", _sharedLockDriver);
        _lockProvider = new CentraDistributedLockProvider(registry);
    }

    [Fact]
    public async Task MultiNode_SimultaneousTickExecution_ShouldExecuteOnlyOnceAcrossCluster()
    {
        // Arrange: 3 simulated nodes sharing the same distributed lock driver
        var nodeIds = new[] { "node-alpha", "node-beta", "node-gamma" };
        var executions = new ConcurrentBag<string>();
        var scheduledTime = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var context = new ScheduledJobContext("cluster-sync-job", scheduledTime, scheduledTime, 1, CancellationToken.None);

        var handlers = nodeIds.Select(nodeId =>
        {
            var innerHandler = new DelegateJobHandler(async ctx =>
            {
                executions.Add(nodeId);
                // Brief work to hold lock while peer nodes attempt acquisition
                await Task.Delay(50, ctx.CancellationToken);
            });

            return new DistributedJobHandler(
                innerHandler,
                _lockProvider,
                "lockstore",
                NullLogger<DistributedJobHandler>.Instance,
                lockTimeout: TimeSpan.FromSeconds(5));
        }).ToList();

        // Act: All 3 nodes attempt to execute the exact same scheduled tick concurrently
        await Task.WhenAll(handlers.Select(h => h.ExecuteAsync(context).AsTask()));

        // Assert: Exactly ONE node won and executed across the cluster; two nodes skipped
        executions.Count.ShouldBe(1);
        nodeIds.ShouldContain(executions.First());
    }

    [Fact]
    public async Task MultiNode_SequentialTicks_ShouldAllowOnlyOneWinnerPerTick()
    {
        var nodeIds = new[] { "node-alpha", "node-beta", "node-gamma" };
        var executionRecords = new ConcurrentBag<(string NodeId, long Iteration)>();

        var handlers = nodeIds.Select(nodeId =>
        {
            var innerHandler = new DelegateJobHandler(async ctx =>
            {
                executionRecords.Add((nodeId, ctx.Iteration));
                await Task.Delay(30, ctx.CancellationToken);
            });

            return new DistributedJobHandler(
                innerHandler,
                _lockProvider,
                "lockstore",
                NullLogger<DistributedJobHandler>.Instance,
                lockTimeout: TimeSpan.FromSeconds(5));
        }).ToList();

        // Execute 3 consecutive ticks
        for (var tick = 1; tick <= 3; tick++)
        {
            var scheduledTime = new DateTimeOffset(2026, 9, 11, 12, 0, tick * 2, TimeSpan.Zero);
            var context = new ScheduledJobContext("cluster-sync-job", scheduledTime, scheduledTime, tick, CancellationToken.None);

            await Task.WhenAll(handlers.Select(h => h.ExecuteAsync(context).AsTask()));
        }

        // Each tick must have exactly 1 winner
        executionRecords.Count.ShouldBe(3);
        executionRecords.Select(r => r.Iteration).OrderBy(i => i).ShouldBe(new long[] { 1, 2, 3 });
    }

    public void Dispose()
    {
        foreach (var scheduler in _schedulers)
        {
            scheduler.Dispose();
        }
    }

    private sealed class DelegateJobHandler(Func<ScheduledJobContext, ValueTask> callback) : IJobHandler
    {
        public ValueTask ExecuteAsync(ScheduledJobContext context) => callback(context);
    }
}
