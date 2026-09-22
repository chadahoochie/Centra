using Centra.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class CentraSerializationServiceCollectionExtensions
{
    public static IServiceCollection AddCentraSerialization(this IServiceCollection services)
    {
        services.TryAddSingleton<ICentraSerializer, JsonCentraSerializer>();
        return services;
    }
}
