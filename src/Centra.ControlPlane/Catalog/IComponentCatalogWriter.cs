using Centra.Components;

namespace Centra.ControlPlane.Catalog;

public interface IComponentCatalogWriter
{
    ValueTask<ComponentCatalogEntry> UpsertComponentAsync(ComponentDefinition definition, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteComponentAsync(string name, CancellationToken cancellationToken = default);
}
