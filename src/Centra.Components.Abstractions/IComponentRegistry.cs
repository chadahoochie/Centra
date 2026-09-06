namespace Centra.Components;

public interface IComponentRegistry
{
    event Action<ComponentDefinition>? ComponentUpdated;
    event Action<string>? ComponentRemoved;

    void RegisterComponent(ComponentDefinition definition);
    void RemoveComponent(string name);
    ComponentDefinition? GetComponent(string name);
    IReadOnlyCollection<ComponentDefinition> GetAllComponents();
    IReadOnlyCollection<ComponentDefinition> GetComponentsByType(ComponentType type);
}
