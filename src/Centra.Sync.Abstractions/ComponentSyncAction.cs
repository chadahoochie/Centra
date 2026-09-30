using System.Text.Json.Serialization;

namespace Centra.Sync;

[JsonConverter(typeof(JsonStringEnumConverter<ComponentSyncAction>))]
public enum ComponentSyncAction
{
    FullSync = 0,
    Added = 1,
    Updated = 2,
    Removed = 3
}
