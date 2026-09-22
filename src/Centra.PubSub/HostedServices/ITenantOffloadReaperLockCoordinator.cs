using Centra.Locks;

namespace Centra.PubSub.HostedServices;

public interface ITenantOffloadReaperLockCoordinator
{
    ValueTask<(bool Acquired, IDistributedLock? Lock)> TryAcquireReaperLockAsync(CancellationToken cancellationToken);
}
