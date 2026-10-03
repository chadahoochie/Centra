using System.Net;

namespace Centra.Providers.Flotilla.Tcp.Client;

/// <summary>
/// Resolves TCP network endpoints from cluster node configuration strings.
/// </summary>
public static class FlotillaTcpEndpointResolver
{
    /// <summary>
    /// Resolves the first reachable IPEndPoint from the provided node strings.
    /// </summary>
    public static IPEndPoint? ResolveTargetEndpoint(string[] clusterNodes, int defaultPort = 9001)
    {
        if (clusterNodes.Length == 0) return null;

        var nodeStr = clusterNodes[0].Trim();
        var schemeIdx = nodeStr.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            nodeStr = nodeStr[(schemeIdx + 3)..];
        }

        if (IPEndPoint.TryParse(nodeStr, out var ep))
        {
            return ep.Port != 0 ? ep : new IPEndPoint(ep.Address, defaultPort);
        }

        var lastColon = nodeStr.LastIndexOf(':');
        if (lastColon > 0)
        {
            var host = nodeStr[..lastColon].Trim('[', ']');
            var portStr = nodeStr[(lastColon + 1)..];
            var parsedPort = int.TryParse(portStr, out var port) ? port : defaultPort;

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return new IPEndPoint(IPAddress.Loopback, parsedPort);
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                return new IPEndPoint(ip, parsedPort);
            }
        }

        if (string.Equals(nodeStr, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return new IPEndPoint(IPAddress.Loopback, defaultPort);
        }

        if (IPAddress.TryParse(nodeStr, out var directIp))
        {
            return new IPEndPoint(directIp, defaultPort);
        }

        return new IPEndPoint(IPAddress.Loopback, defaultPort);
    }
}
