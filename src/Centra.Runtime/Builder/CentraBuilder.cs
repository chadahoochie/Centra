using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Default builder implementation for Centra configuration and component registration.
/// </summary>
public sealed class CentraBuilder : ICentraBuilder
{
    public IServiceCollection Services { get; }
    public IConfiguration? Configuration { get; }

    public CentraBuilder(IServiceCollection services, IConfiguration? configuration = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Configuration = configuration;
    }
}
