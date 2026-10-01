using Centra.Drivers;
using Centra.Registry;

namespace Centra.Providers.Flotilla.Grpc.Extensions;

/// <summary>
/// Delegate-based component initializer for named Flotilla gRPC pub/sub registrations.
/// </summary>
public sealed class FlotillaGrpcDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public FlotillaGrpcDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry)
    {
        _action(registry);
    }
}
