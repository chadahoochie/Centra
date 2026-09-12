using Centra.Invocation;
using Microsoft.Extensions.Configuration;

namespace Centra.Hosting.Invocation;

public sealed class ConfigurationServiceEndpointResolver : IServiceEndpointResolver
{
    private readonly IConfiguration? _configuration;
    private readonly IServiceEndpointResolver _fallback;

    public ConfigurationServiceEndpointResolver(
        IConfiguration? configuration,
        IServiceEndpointResolver? fallback = null)
    {
        _configuration = configuration;
        _fallback = fallback ?? PassThroughServiceEndpointResolver.Instance;
    }

    public ValueTask<Uri?> ResolveEndpointAsync(string serviceAppId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceAppId);

        if (_configuration is not null)
        {
            // 1. Explicit Centra configuration key: Centra:Services:{serviceAppId}:Address
            var explicitAddress = _configuration[$"Centra:Services:{serviceAppId}:Address"];
            if (!string.IsNullOrWhiteSpace(explicitAddress) && Uri.TryCreate(EnsureTrailingSlash(explicitAddress), UriKind.Absolute, out var explicitUri))
            {
                return ValueTask.FromResult<Uri?>(explicitUri);
            }

            // 2. Aspire standard service reference: services:{serviceAppId}:http:0 or https:0
            var aspireHttp = _configuration[$"services:{serviceAppId}:http:0"];
            if (!string.IsNullOrWhiteSpace(aspireHttp) && Uri.TryCreate(EnsureTrailingSlash(aspireHttp), UriKind.Absolute, out var aspireHttpUri))
            {
                return ValueTask.FromResult<Uri?>(aspireHttpUri);
            }

            var aspireHttps = _configuration[$"services:{serviceAppId}:https:0"];
            if (!string.IsNullOrWhiteSpace(aspireHttps) && Uri.TryCreate(EnsureTrailingSlash(aspireHttps), UriKind.Absolute, out var aspireHttpsUri))
            {
                return ValueTask.FromResult<Uri?>(aspireHttpsUri);
            }
        }

        return _fallback.ResolveEndpointAsync(serviceAppId, cancellationToken);
    }

    internal static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
