using Centra.Components;
using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.RabbitMQ.Extensions;

public sealed class RabbitMQComponentInitializer : IComponentInitializer
{
    private readonly RabbitMQPubSubDriver _pubSubDriver;
    private readonly IOptions<RabbitMQProviderOptions> _options;

    public RabbitMQComponentInitializer(
        RabbitMQPubSubDriver pubSubDriver,
        IOptions<RabbitMQProviderOptions> options)
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
            Provider = "rabbitmq"
        });
    }
}
