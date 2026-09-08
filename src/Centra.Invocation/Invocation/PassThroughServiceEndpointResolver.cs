namespace Centra.Invocation;

public sealed class PassThroughServiceEndpointResolver : IServiceEndpointResolver
{
    public static readonly PassThroughServiceEndpointResolver Instance = new();

    public ValueTask<Uri?> ResolveEndpointAsync(string serviceAppId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceAppId);
        return ValueTask.FromResult<Uri?>(new Uri($"http://{serviceAppId}/", UriKind.Absolute));
    }
}
