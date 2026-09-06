using Centra.Components;

namespace Centra.ControlPlane.Catalog;

public interface IComponentCatalogReader
{
    ValueTask<ComponentCatalogEntry?> GetComponentAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<ComponentCatalogEntry>> GetAllComponentsAsync(CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<ComponentCatalogEntry>> GetComponentsByTypeAsync(ComponentType type, CancellationToken cancellationToken = default);
    ValueTask<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default);
}
