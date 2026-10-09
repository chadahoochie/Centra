using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Centra.ControlPlane.Dashboard;

public static class ControlPlaneDashboardEndpointExtensions
{
    public static IEndpointRouteBuilder MapCentraDashboard(
        this IEndpointRouteBuilder endpoints,
        ControlPlaneDashboardOptions? options = null)
    {
        var opt = options ?? new ControlPlaneDashboardOptions();
        if (!opt.Enabled)
        {
            return endpoints;
        }

        var path = opt.Path.TrimEnd('/');
        var dashboardDir = DashboardFileLocator.FindDashboardRoot();

        endpoints.MapGet(path, () =>
        {
            if (dashboardDir is not null)
            {
                var indexPath = Path.Combine(dashboardDir, "index.html");
                if (File.Exists(indexPath))
                {
                    return Results.File(indexPath, "text/html; charset=utf-8");
                }
            }

            return Results.Content(DashboardHtmlProvider.GetDashboardHtml(opt.Title), "text/html");
        });

        endpoints.MapGet($"{path}/{{*rest}}", (string? rest) =>
        {
            if (dashboardDir is not null && !string.IsNullOrWhiteSpace(rest))
            {
                var safeSubPath = rest.Replace("..", string.Empty).TrimStart('/');
                var candidateFile = Path.Combine(dashboardDir, safeSubPath);
                if (File.Exists(candidateFile))
                {
                    var contentType = DashboardContentTypeResolver.GetContentType(candidateFile);
                    return Results.File(candidateFile, contentType);
                }

                var indexPath = Path.Combine(dashboardDir, "index.html");
                if (File.Exists(indexPath))
                {
                    return Results.File(indexPath, "text/html; charset=utf-8");
                }
            }

            return Results.Content(DashboardHtmlProvider.GetDashboardHtml(opt.Title), "text/html");
        });

        return endpoints;
    }
}
