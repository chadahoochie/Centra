using Centra.Invocation;

namespace Centra.Sample.DockerStack.Domain;

[ServiceClient("dockerstack-node")]
public interface IPeerClient
{
    [ServiceMethod("instance", "GET")]
    Task<ClusterNodeInfo> GetNodeInfoAsync();
}
