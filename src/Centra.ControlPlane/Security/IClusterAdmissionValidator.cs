using Microsoft.AspNetCore.Http;

namespace Centra.ControlPlane.Security;

public interface IClusterAdmissionValidator
{
    ValueTask<ClusterAdmissionResult> ValidateAsync(HttpContext httpContext, string clusterId, CancellationToken cancellationToken = default);
}
