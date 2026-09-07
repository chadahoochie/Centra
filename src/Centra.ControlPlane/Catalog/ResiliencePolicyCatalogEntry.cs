using Centra.Sync;

namespace Centra.ControlPlane.Catalog;

/// <summary>
/// Represents an entry in the Control Plane resilience policy catalog.
/// </summary>
public sealed record ResiliencePolicyCatalogEntry(
    ResiliencePolicyDto Policy,
    long Revision,
    DateTimeOffset UpdatedAtUtc);
