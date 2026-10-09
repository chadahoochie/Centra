namespace Centra.ControlPlane.Topology;

public readonly record struct CompositeClusterKey(string ClusterId, string AppId, string InstanceId) : IEquatable<CompositeClusterKey>;
