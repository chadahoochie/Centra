namespace Centra.Invocation;

public interface IServiceEndpointResolver
{
    ValueTask<Uri?> ResolveEndpointAsync(string serviceAppId, CancellationToken cancellationToken = default);
}
