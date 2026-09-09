namespace Centra.Core.Actors;

/// <summary>
/// Represents the immutable snapshot of hashed node partitions in a consistent hash ring.
/// </summary>
public sealed record ConsistentHashRingState(uint[] Hashes, string[] NodeIds);
