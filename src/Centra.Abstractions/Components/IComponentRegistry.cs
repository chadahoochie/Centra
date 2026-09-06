namespace Centra.Components;

public interface IComponentRegistry
{
    void RegisterComponent(ComponentDefinition definition);
    ComponentDefinition? GetComponent(string name);
    IReadOnlyCollection<ComponentDefinition> GetAllComponents();
    IReadOnlyCollection<ComponentDefinition> GetComponentsByType(ComponentType type);
}
