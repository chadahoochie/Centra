namespace Centra.Locks;

public interface IDistributedLock : IAsyncDisposable
{
    string ResourceId { get; }
    string LockId { get; }
    ValueTask<bool> RenewAsync(TimeSpan additionalTime, CancellationToken cancellationToken = default);
}
