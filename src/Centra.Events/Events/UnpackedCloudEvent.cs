using Centra.Events;

namespace Centra.Events;

public readonly record struct UnpackedCloudEvent<T>(
    T? Data,
    EventContext Context);
