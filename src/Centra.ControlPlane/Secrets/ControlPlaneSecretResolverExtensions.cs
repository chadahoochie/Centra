using Centra.Components;
using Centra.ControlPlane.Catalog;

namespace Centra.ControlPlane.Secrets;

/// <summary>
/// Provides extension methods for batch secret resolution on component definitions.
/// </summary>
public static class ControlPlaneSecretResolverExtensions
{
    /// <summary>
    /// Resolves secrets for a collection of component catalog entries asynchronously.
    /// </summary>
    public static async ValueTask<List<ComponentDefinition>> ResolveComponentDefinitionsAsync(
        this IControlPlaneSecretResolver secretResolver,
        IEnumerable<ComponentCatalogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretResolver);
        ArgumentNullException.ThrowIfNull(entries);

        var resolved = new List<ComponentDefinition>();
        foreach (var entry in entries)
        {
            var def = await secretResolver.ResolveSecretsAsync(entry.Definition, cancellationToken).ConfigureAwait(false);
            resolved.Add(def);
        }

        return resolved;
    }
}
