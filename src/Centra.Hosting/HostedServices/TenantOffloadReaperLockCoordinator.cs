using Centra.Locks;
using Centra.PubSub.Tenancy;

namespace Centra.Hosting.HostedServices;

public sealed class TenantOffloadReaperLockCoordinator : ITenantOffloadReaperLockCoordinator
{
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly TenantOffloadOptions _options;

    public TenantOffloadReaperLockCoordinator(
        IDistributedLockProvider? lockProvider,
        TenantOffloadOptions options)
    {
        _lockProvider = lockProvider;
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<(bool Acquired, IDistributedLock? Lock)> TryAcquireReaperLockAsync(CancellationToken cancellationToken)
    {
        if (_lockProvider is null || !_options.EnableDistributedReaperLock)
        {
            return (true, null);
        }

        try
        {
            var @lock = await _lockProvider.TryAcquireLockAsync(
                "lockstore",
                "centra:reaper:tenant-offload",
                expiryTime: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return (@lock is not null, @lock);
        }
        catch (Exception)
        {
            return (false, null);
        }
    }
}
