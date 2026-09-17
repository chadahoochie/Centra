using Centra.Locks;

namespace Centra.Hosting.HostedServices;

public interface ITenantOffloadReaperLockCoordinator
{
    ValueTask<(bool Acquired, IDistributedLock? Lock)> TryAcquireReaperLockAsync(CancellationToken cancellationToken);
}
