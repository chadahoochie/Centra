using Centra.Serialization;

namespace Centra.PubSub;

public sealed record PubSubSerializerRegistration(
    string ContentType,
    Type? SerializerType,
    ICentraSerializer? SerializerInstance);
