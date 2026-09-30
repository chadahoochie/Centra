using Centra.Components;
using Centra.Providers.Flotilla.Options;
using Centra.Providers.Flotilla.PubSub;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Hosting.Extensions;

/// <summary>
/// Registers Flotilla pub/sub components with Centra's component registry.
/// </summary>
public sealed class FlotillaComponentInitializer : IComponentInitializer
{
    private readonly FlotillaPubSubDriver _pubSubDriver;
    private readonly IOptions<FlotillaProviderOptions> _options;

    public FlotillaComponentInitializer(
        FlotillaPubSubDriver pubSubDriver,
        IOptions<FlotillaProviderOptions> options)
    {
        _pubSubDriver = pubSubDriver ?? throw new ArgumentNullException(nameof(pubSubDriver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Initialize(ComponentRegistry registry)
    {
        var opts = _options.Value;

        registry.RegisterPubSubDriver(opts.DefaultPubSubName, _pubSubDriver);

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultPubSubName,
            Type = ComponentType.PubSub,
            Provider = "flotilla"
        });
    }
}
