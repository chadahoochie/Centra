using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Config builder contract for registering and configuring Centra distributed application components.
/// </summary>
public interface ICentraBuilder
{
    /// <summary>
    /// Gets the application service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Gets the application configuration, if available.
    /// </summary>
    IConfiguration? Configuration { get; }
}
