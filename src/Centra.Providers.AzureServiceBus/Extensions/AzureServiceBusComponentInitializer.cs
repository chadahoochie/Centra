using Centra.Components;
using Centra.Providers.AzureServiceBus.Options;
using Centra.Providers.AzureServiceBus.PubSub;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.AzureServiceBus.Extensions;

public sealed class AzureServiceBusComponentInitializer : IComponentInitializer
{
    private readonly AzureServiceBusPubSubDriver _pubSubDriver;
    private readonly IOptions<AzureServiceBusProviderOptions> _options;

    public AzureServiceBusComponentInitializer(
        AzureServiceBusPubSubDriver pubSubDriver,
        IOptions<AzureServiceBusProviderOptions> options)
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
            Provider = "azureservicebus"
        });
    }
}
