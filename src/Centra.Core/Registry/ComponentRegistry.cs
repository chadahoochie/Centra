using System.Collections.Concurrent;
using Centra.Components;
using Centra.Drivers;

namespace Centra.Registry;

public sealed class ComponentRegistry : IComponentRegistry
{
    private readonly ConcurrentDictionary<string, ComponentDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IStateStoreDriver> _stateStoreDrivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPubSubDriver> _pubSubDrivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IDistributedLockDriver> _lockDrivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IBindingDriver> _bindingDrivers = new(StringComparer.OrdinalIgnoreCase);

    public event Action<ComponentDefinition>? ComponentUpdated;
    public event Action<string>? ComponentRemoved;

    public void RegisterComponent(ComponentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions[definition.Name] = definition;
        ComponentUpdated?.Invoke(definition);
    }

    public void RemoveComponent(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_definitions.TryRemove(name, out _))
        {
            _stateStoreDrivers.TryRemove(name, out _);
            _pubSubDrivers.TryRemove(name, out _);
            _lockDrivers.TryRemove(name, out _);
            _bindingDrivers.TryRemove(name, out _);
            ComponentRemoved?.Invoke(name);
        }
    }

    public ComponentDefinition? GetComponent(string name)
    {
        _definitions.TryGetValue(name, out var def);
        return def;
    }

    public IReadOnlyCollection<ComponentDefinition> GetAllComponents() => _definitions.Values.ToArray();

    public IReadOnlyCollection<ComponentDefinition> GetComponentsByType(ComponentType type) =>
        _definitions.Values.Where(d => d.Type == type).ToArray();

    // Driver registrations
    public void RegisterStateStoreDriver(string storeName, IStateStoreDriver driver)
    {
        _stateStoreDrivers[storeName] = driver;
    }

    public IStateStoreDriver? GetStateStoreDriver(string storeName)
    {
        _stateStoreDrivers.TryGetValue(storeName, out var driver);
        return driver;
    }

    public void RegisterPubSubDriver(string pubSubName, IPubSubDriver driver)
    {
        _pubSubDrivers[pubSubName] = driver;
    }

    public IPubSubDriver? GetPubSubDriver(string pubSubName)
    {
        _pubSubDrivers.TryGetValue(pubSubName, out var driver);
        return driver;
    }

    public void RegisterLockDriver(string lockStoreName, IDistributedLockDriver driver)
    {
        _lockDrivers[lockStoreName] = driver;
    }

    public IDistributedLockDriver? GetLockDriver(string lockStoreName)
    {
        _lockDrivers.TryGetValue(lockStoreName, out var driver);
        return driver;
    }

    public void RegisterBindingDriver(string bindingName, IBindingDriver driver)
    {
        _bindingDrivers[bindingName] = driver;
    }

    public IBindingDriver? GetBindingDriver(string bindingName)
    {
        _bindingDrivers.TryGetValue(bindingName, out var driver);
        return driver;
    }
}
