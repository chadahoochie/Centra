using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Grpc.Options;
using Centra.Providers.Flotilla.PubSub;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Grpc.Extensions;

/// <summary>
/// Initializes the Flotilla gRPC pub/sub component in the Centra component registry.
/// </summary>
public sealed class FlotillaGrpcComponentInitializer : IComponentInitializer
{
    private readonly FlotillaPubSubDriver _pubSubDriver;
    private readonly IOptions<FlotillaGrpcOptions> _options;

    public FlotillaGrpcComponentInitializer(
        FlotillaPubSubDriver pubSubDriver,
        IOptions<FlotillaGrpcOptions> options)
    {
        _pubSubDriver = pubSubDriver ?? throw new ArgumentNullException(nameof(pubSubDriver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Initialize(ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var pubSubName = _options.Value.DefaultPubSubName;
        registry.RegisterPubSubDriver(pubSubName, _pubSubDriver);

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = pubSubName,
            Type = ComponentType.PubSub,
            Provider = "flotilla-grpc"
        });
    }
}
