namespace Centra.Core.Actors;

/// <summary>
/// Strategy interface for hashing keys to positions on the consistent hash ring.
/// </summary>
public interface IConsistentHashAlgorithm
{
    /// <summary>
    /// Computes a 32-bit unsigned integer hash for the specified key.
    /// </summary>
    uint ComputeHash(string key);
}
