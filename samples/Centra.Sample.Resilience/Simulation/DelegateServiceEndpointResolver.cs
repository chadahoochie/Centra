using Centra.Invocation;

namespace Centra.Sample.Resilience.Simulation;

public sealed class DelegateServiceEndpointResolver : IServiceEndpointResolver
{
    private readonly Func<string, Uri?> _resolver;

    public DelegateServiceEndpointResolver(Func<string, Uri?> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public ValueTask<Uri?> ResolveEndpointAsync(string serviceAppId, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_resolver(serviceAppId));
    }
}
