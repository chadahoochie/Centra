namespace Centra.Providers.Flotilla.Grpc.Client;

/// <summary>
/// Resolves gRPC channel endpoint URIs from cluster node configuration strings.
/// </summary>
public static class FlotillaGrpcEndpointResolver
{
    /// <summary>
    /// Resolves the first valid gRPC URI from the provided node strings.
    /// </summary>
    public static Uri ResolveTargetUri(string[] clusterNodes, int defaultPort = 9001)
    {
        if (clusterNodes.Length == 0)
        {
            return new Uri($"http://127.0.0.1:{defaultPort}");
        }

        var nodeStr = clusterNodes[0].Trim();
        if (!nodeStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !nodeStr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            nodeStr = $"http://{nodeStr}";
        }

        if (Uri.TryCreate(nodeStr, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        return new Uri($"http://127.0.0.1:{defaultPort}");
    }
}
