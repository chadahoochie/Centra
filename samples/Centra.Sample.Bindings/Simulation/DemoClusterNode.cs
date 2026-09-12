using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Centra.Sample.Bindings.Simulation;

/// <summary>
/// Encapsulates a running application node within the simulated cluster.
/// </summary>
public sealed record DemoClusterNode(
    string NodeId,
    WebApplication App)
{
    /// <summary>
    /// Creates an in-memory test client to dispatch HTTP requests to this node.
    /// </summary>
    public HttpClient CreateClient() => App.GetTestServer().CreateClient();
}
