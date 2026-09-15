using System.Collections.Concurrent;
using Centra.Diagnostics;
using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public sealed class InProcessFairSchedulerOffloadStrategy : ITenantOffloadStrategy, IAsyncDisposable
{
    private static readonly Func<TenantTopicKey, (int Capacity, int Concurrency), TenantWorkerLane> LaneFactory =
        static (key, state) => new TenantWorkerLane(key.TenantId, key.Topic, state.Capacity, state.Concurrency);

    private readonly TenantOffloadOptions _options;
    private readonly ConcurrentDictionary<TenantTopicKey, TenantWorkerLane> _lanes = new();
    private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _dispatcherTask;
    private bool _isDisposed;

    public TenantOffloadStrategyType StrategyType => TenantOffloadStrategyType.InProcessFairScheduler;

    public InProcessFairSchedulerOffloadStrategy(TenantOffloadOptions? options = null)
    {
        _options = options ?? new TenantOffloadOptions();
        _dispatcherTask = Task.Run(() => RunDispatcherLoopAsync(_cts.Token));
    }

    public async ValueTask<EventHandlingResult> ExecuteOffloadAsync(TenantOffloadWorkItem workItem, CancellationToken cancellationToken)
    {
        var lane = GetOrCreateLane(workItem.TenantId, workItem.Topic);
        var tcs = new TaskCompletionSource<EventHandlingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var itemWithTcs = workItem with { CompletionSource = tcs };

        await lane.EnqueueAsync(itemWithTcs, cancellationToken).ConfigureAwait(false);
        _wakeSignal.Release();

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task.ConfigureAwait(false);
    }

    public string ResolvePublishTopic(string baseTopic, string tenantId)
    {
        return baseTopic;
    }

    public async ValueTask CleanupIdleResourcesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _lanes)
        {
            var lane = kvp.Value;
            if (lane.QueueDepth == 0 && lane.ActiveExecutions == 0)
            {
                if (now - lane.LastActivityTime >= _options.LaneIdleTimeout)
                {
                    if (_lanes.TryRemove(kvp.Key, out var removedLane))
                    {
                        await removedLane.DisposeAsync().ConfigureAwait(false);
                        CentraMeters.RecordTenantLaneReaped(removedLane.TenantId, removedLane.Topic);
                    }
                }
            }
        }
    }

    internal TenantWorkerLane GetOrCreateLane(string tenantId, string topic)
    {
        var key = new TenantTopicKey(topic, tenantId);
        return _lanes.GetOrAdd(key, LaneFactory, (_options.PerTenantQueueCapacity, _options.MaxConcurrencyPerTenant));
    }

    internal async Task RunDispatcherLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _wakeSignal.WaitAsync(100, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var processedAny = false;
            foreach (var lane in _lanes.Values)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (lane.ConcurrencySemaphore.CurrentCount > 0 && lane.Reader.TryRead(out var item))
                {
                    processedAny = true;
                    _ = TenantWorkItemProcessor.ProcessAsync(lane, item, _wakeSignal, cancellationToken);
                }
            }

            if (processedAny && HasPendingItems())
            {
                _wakeSignal.Release();
            }
        }
    }

    internal bool HasPendingItems()
    {
        foreach (var lane in _lanes.Values)
        {
            if (lane.QueueDepth > 0)
            {
                return true;
            }
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cts.Cancel();

        try
        {
            await _dispatcherTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var lane in _lanes.Values)
        {
            await lane.DisposeAsync().ConfigureAwait(false);
        }

        _lanes.Clear();
        _wakeSignal.Dispose();
        _cts.Dispose();
    }
}
