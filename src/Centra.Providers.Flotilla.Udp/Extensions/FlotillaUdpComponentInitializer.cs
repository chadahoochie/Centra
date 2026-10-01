using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.PubSub;
using Centra.Providers.Flotilla.Udp.Options;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Udp.Extensions;

/// <summary>
/// Initializes the Flotilla UDP pub/sub component in the Centra component registry.
/// </summary>
public sealed class FlotillaUdpComponentInitializer : IComponentInitializer
{
    private readonly FlotillaPubSubDriver _pubSubDriver;
    private readonly IOptions<FlotillaUdpOptions> _options;

    public FlotillaUdpComponentInitializer(
        FlotillaPubSubDriver pubSubDriver,
        IOptions<FlotillaUdpOptions> options)
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
            Provider = "flotilla-udp"
        });
    }
}
