using Centra.Invocation;

namespace Centra.Sample.MultiInstance.Domain;

[ServiceClient("multi-instance-service")]
public interface INodePeerClient
{
    [ServiceMethod("instance", "GET")]
    Task<ClusterNodeInfo> GetNodeInfoAsync();
}
