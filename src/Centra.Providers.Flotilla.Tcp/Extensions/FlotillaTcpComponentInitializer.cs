using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.PubSub;
using Centra.Providers.Flotilla.Tcp.Options;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Tcp.Extensions;

/// <summary>
/// Initializes the Flotilla TCP pub/sub component in the Centra component registry.
/// </summary>
public sealed class FlotillaTcpComponentInitializer : IComponentInitializer
{
    private readonly FlotillaPubSubDriver _pubSubDriver;
    private readonly IOptions<FlotillaTcpOptions> _options;

    public FlotillaTcpComponentInitializer(
        FlotillaPubSubDriver pubSubDriver,
        IOptions<FlotillaTcpOptions> options)
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
            Provider = "flotilla-tcp"
        });
    }
}
