namespace Centra.Sample.Bindings.Services;

/// <summary>
/// Implementation of <see cref="INodeContext"/> holding this node's instance identifier.
/// </summary>
public sealed class NodeContext(string nodeId) : INodeContext
{
    public string NodeId { get; } = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
}
