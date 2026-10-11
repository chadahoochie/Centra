using System.Diagnostics.CodeAnalysis;
using Centra.Serialization;

namespace Centra.PubSub;

public interface IPubSubSerializerRegistry
{
    void Register(string contentType, ICentraSerializer serializer);
    ICentraSerializer? GetSerializer(string contentType);
    bool TryGetSerializer(string contentType, [NotNullWhen(true)] out ICentraSerializer? serializer);
}
