using System.Text.Json.Serialization;

namespace Centra.Components;

[JsonConverter(typeof(JsonStringEnumConverter<ComponentType>))]
public enum ComponentType
{
    StateStore,
    PubSub,
    DistributedLock,
    Binding,
    SecretStore
}
