using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public static class TenantWorkItemProcessor
{
    public static async Task ProcessAsync(
        TenantWorkerLane lane,
        TenantOffloadWorkItem item,
        SemaphoreSlim? wakeSignal,
        CancellationToken cancellationToken)
    {
        await lane.ConcurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        lane.IncrementActiveExecutions();

        try
        {
            var result = await item.HandlerInvoker(cancellationToken).ConfigureAwait(false);
            item.CompletionSource?.TrySetResult(result);
        }
        catch (Exception ex)
        {
            item.CompletionSource?.TrySetException(ex);
        }
        finally
        {
            lane.DecrementActiveExecutions();
            try
            {
                lane.ConcurrencySemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed during shutdown drain; ignore
            }

            try
            {
                wakeSignal?.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed during shutdown drain; ignore
            }
        }
    }
}
