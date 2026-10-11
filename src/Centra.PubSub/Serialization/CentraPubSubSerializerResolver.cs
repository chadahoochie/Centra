using Centra.Events;
using Centra.PubSub.Routing;
using Centra.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Centra.PubSub;

public static class CentraPubSubSerializerResolver
{
    public static ICentraSerializer Resolve(
        IServiceProvider serviceProvider,
        CentraTopicRegistration reg,
        IReadOnlyDictionary<string, string> headers)
    {
        if (reg.Serializer is not null)
        {
            return reg.Serializer;
        }

        if (reg.SerializerType is not null)
        {
            if (serviceProvider.GetService(reg.SerializerType) is ICentraSerializer typedSerializer)
            {
                return typedSerializer;
            }

            return (ICentraSerializer)ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, reg.SerializerType);
        }

        var registry = serviceProvider.GetService<IPubSubSerializerRegistry>();
        if (registry is not null)
        {
            if (headers.TryGetValue(CloudEventConstants.DataContentTypeHeader, out var ct) &&
                registry.TryGetSerializer(ct, out var ctSerializer))
            {
                return ctSerializer;
            }

            if (reg.ContentType is not null && registry.TryGetSerializer(reg.ContentType, out var regCtSerializer))
            {
                return regCtSerializer;
            }
        }

        return serviceProvider.GetService<IPubSubSerializer>()
            ?? serviceProvider.GetService<ICentraSerializer>()
            ?? JsonCentraSerializer.Default;
    }
}
