namespace Centra.Sample.Bindings.Services;

/// <summary>
/// Provides identity information for an individual application node in a multi-instance cluster.
/// </summary>
public interface INodeContext
{
    /// <summary>
    /// The unique identifier of this node instance (e.g. "node-alpha", "node-beta", "node-gamma").
    /// </summary>
    string NodeId { get; }
}
