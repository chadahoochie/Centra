using System.Threading.Channels;

namespace Centra.PubSub.Tenancy;

public sealed class TenantWorkerLane : IAsyncDisposable
{
    private readonly Channel<TenantOffloadWorkItem> _channel;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private int _activeExecutions;
    private bool _isDisposed;

    public string TenantId { get; }

    public string Topic { get; }

    public DateTimeOffset LastActivityTime { get; set; }

    public int ActiveExecutions => Volatile.Read(ref _activeExecutions);

    public int QueueDepth => _channel.Reader.Count;

    public SemaphoreSlim ConcurrencySemaphore => _concurrencySemaphore;

    public ChannelReader<TenantOffloadWorkItem> Reader => _channel.Reader;

    public TenantWorkerLane(string tenantId, string topic, int capacity = 500, int maxConcurrency = 2)
    {
        TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        LastActivityTime = DateTimeOffset.UtcNow;

        _channel = Channel.CreateBounded<TenantOffloadWorkItem>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        _concurrencySemaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));
    }

    public async ValueTask EnqueueAsync(TenantOffloadWorkItem item, CancellationToken cancellationToken = default)
    {
        LastActivityTime = DateTimeOffset.UtcNow;
        await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public bool TryEnqueue(TenantOffloadWorkItem item)
    {
        LastActivityTime = DateTimeOffset.UtcNow;
        return _channel.Writer.TryWrite(item);
    }

    public void IncrementActiveExecutions()
    {
        LastActivityTime = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _activeExecutions);
    }

    public void DecrementActiveExecutions()
    {
        LastActivityTime = DateTimeOffset.UtcNow;
        Interlocked.Decrement(ref _activeExecutions);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _channel.Writer.TryComplete();

        // Drain any remaining items with drop result
        while (_channel.Reader.TryRead(out var item))
        {
            item.CompletionSource?.TrySetResult(EventHandlingResult.Drop);
        }

        // Wait for active in-flight worker executions to drain before disposing semaphore
        var spinCount = 0;
        while (ActiveExecutions > 0 && spinCount < 100)
        {
            await Task.Delay(10).ConfigureAwait(false);
            spinCount++;
        }

        _concurrencySemaphore.Dispose();
    }
}
