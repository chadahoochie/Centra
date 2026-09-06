using Centra.Components;

namespace Centra.ControlPlane.Catalog;

public sealed record ComponentCatalogEntry(
    ComponentDefinition Definition,
    long Revision,
    DateTimeOffset UpdatedAtUtc);
