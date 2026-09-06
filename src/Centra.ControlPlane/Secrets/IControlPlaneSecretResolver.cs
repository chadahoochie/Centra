using Centra.Components;

namespace Centra.ControlPlane.Secrets;

public interface IControlPlaneSecretResolver
{
    ValueTask<ComponentDefinition> ResolveSecretsAsync(ComponentDefinition definition, CancellationToken cancellationToken = default);
}
