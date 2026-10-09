using Centra.ControlPlane.Serialization;
using Microsoft.AspNetCore.Http;

namespace Centra.ControlPlane.Security;

public sealed class ClusterAuthenticationEndpointFilter : IEndpointFilter
{
    private readonly IClusterAdmissionValidator _validator;

    public ClusterAuthenticationEndpointFilter(IClusterAdmissionValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Request.Path.StartsWithSegments("/api/v1/health"))
        {
            return await next(context).ConfigureAwait(false);
        }

        var clusterId = httpContext.Request.Headers["X-Centra-Cluster-Id"].ToString();
        if (string.IsNullOrWhiteSpace(clusterId) && httpContext.Request.Query.TryGetValue("clusterId", out var queryClusterId))
        {
            clusterId = queryClusterId.ToString();
        }

        if (string.IsNullOrWhiteSpace(clusterId))
        {
            clusterId = "default";
        }

        var result = await _validator.ValidateAsync(httpContext, clusterId, httpContext.RequestAborted).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Json(
                new ClusterAdmissionErrorResponse(result.ErrorMessage ?? "Unauthorized"),
                ControlPlaneJsonSerializerContext.Default.ClusterAdmissionErrorResponse,
                statusCode: result.StatusCode);
        }

        return await next(context).ConfigureAwait(false);
    }
}
