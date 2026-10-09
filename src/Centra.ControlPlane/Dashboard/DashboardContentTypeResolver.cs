using System.IO;

namespace Centra.ControlPlane.Dashboard;

public static class DashboardContentTypeResolver
{
    public static string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.ToLowerInvariant() switch
        {
            ".js" => "application/javascript",
            ".css" => "text/css",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".json" => "application/json",
            ".txt" => "text/plain; charset=utf-8",
            ".png" => "image/png",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }
}
