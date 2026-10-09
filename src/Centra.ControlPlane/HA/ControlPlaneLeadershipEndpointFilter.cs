using Microsoft.AspNetCore.Http;

namespace Centra.ControlPlane.HA;

public sealed class ControlPlaneLeadershipEndpointFilter : IEndpointFilter
{
    private readonly ControlPlaneLeadershipOptions _options;
    private readonly IControlPlaneLeaderTracker _leaderTracker;

    public ControlPlaneLeadershipEndpointFilter(
        ControlPlaneLeadershipOptions options,
        IControlPlaneLeaderTracker leaderTracker)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _leaderTracker = leaderTracker ?? throw new ArgumentNullException(nameof(leaderTracker));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!_options.Enabled || _leaderTracker.IsLeader || httpContext.Request.Path.StartsWithSegments("/api/v1/health"))
        {
            return await next(context).ConfigureAwait(false);
        }

        var leaderEndpoint = _leaderTracker.LeaderEndpoint;

        if (string.IsNullOrWhiteSpace(leaderEndpoint))
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var targetUrl = $"{leaderEndpoint.TrimEnd('/')}{httpContext.Request.Path}{httpContext.Request.QueryString}";
        httpContext.Response.Headers["Location"] = targetUrl;
        httpContext.Response.Headers["X-Centra-Role"] = "Standby";
        httpContext.Response.Headers["X-Centra-Leader"] = leaderEndpoint;
        httpContext.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;

        return Results.Redirect(targetUrl, permanent: false, preserveMethod: true);
    }
}
