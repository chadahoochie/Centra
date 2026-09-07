using Centra.Components;
using Centra.Providers.InMemory.Bindings;
using Centra.Providers.InMemory.Locks;
using Centra.Providers.InMemory.PubSub;
using Centra.Providers.InMemory.State;
using Centra.Registry;

namespace Centra.Providers.InMemory.Extensions;

public sealed class InMemoryComponentInitializer : IComponentInitializer
{
    private readonly InMemoryStateStoreDriver _stateDriver;
    private readonly InMemoryPubSubDriver _pubSubDriver;
    private readonly InMemoryDistributedLockDriver _lockDriver;
    private readonly InMemoryBindingDriver _bindingDriver;
    private readonly string _defaultStateStore;
    private readonly string _defaultPubSub;
    private readonly string _defaultLockStore;

    public InMemoryComponentInitializer(
        InMemoryStateStoreDriver stateDriver,
        InMemoryPubSubDriver pubSubDriver,
        InMemoryDistributedLockDriver lockDriver,
        InMemoryBindingDriver bindingDriver,
        string defaultStateStore,
        string defaultPubSub,
        string defaultLockStore)
    {
        _stateDriver = stateDriver;
        _pubSubDriver = pubSubDriver;
        _lockDriver = lockDriver;
        _bindingDriver = bindingDriver;
        _defaultStateStore = defaultStateStore;
        _defaultPubSub = defaultPubSub;
        _defaultLockStore = defaultLockStore;
    }

    public void Initialize(ComponentRegistry registry)
    {
        registry.RegisterStateStoreDriver(_defaultStateStore, _stateDriver);
        registry.RegisterPubSubDriver(_defaultPubSub, _pubSubDriver);
        registry.RegisterLockDriver(_defaultLockStore, _lockDriver);
        registry.RegisterBindingDriver("binding", _bindingDriver);
        registry.RegisterBindingDriver("in-memory-binding", _bindingDriver);

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = "binding",
            Type = ComponentType.Binding,
            Provider = "in-memory"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = _defaultStateStore,
            Type = ComponentType.StateStore,
            Provider = "in-memory"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = _defaultPubSub,
            Type = ComponentType.PubSub,
            Provider = "in-memory"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = _defaultLockStore,
            Type = ComponentType.DistributedLock,
            Provider = "in-memory"
        });
    }
}
