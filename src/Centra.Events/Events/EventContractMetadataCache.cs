using System.Reflection;

namespace Centra.Events;

internal static class EventContractMetadataCache<T>
{
    private static readonly EventContractAttribute? Attribute = typeof(T).GetCustomAttribute<EventContractAttribute>();

    public static readonly string EventType = Attribute?.Type ?? typeof(T).Name;
    public static readonly string? SchemaVersion = Attribute?.Version;
    public static readonly string? DataSchema = Attribute?.Schema;
}
