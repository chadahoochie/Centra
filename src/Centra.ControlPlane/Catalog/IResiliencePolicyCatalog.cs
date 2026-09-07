using Centra.Sync;

namespace Centra.ControlPlane.Catalog;

/// <summary>
/// Defines the catalog storage operations for resilience policies in the Centra Control Plane.
/// </summary>
public interface IResiliencePolicyCatalog
{
    ValueTask<ResiliencePolicyCatalogEntry?> GetPolicyAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<ResiliencePolicyCatalogEntry>> GetAllPoliciesAsync(CancellationToken cancellationToken = default);
    ValueTask<ResiliencePolicyCatalogEntry> UpsertPolicyAsync(ResiliencePolicyDto policy, CancellationToken cancellationToken = default);
    ValueTask<bool> DeletePolicyAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default);
}
