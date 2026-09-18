using System.Text;

namespace Centra.Core.Actors;

/// <summary>
/// FNV-1a 32-bit non-cryptographic implementation of <see cref="IConsistentHashAlgorithm"/> with zero heap allocations.
/// </summary>
public sealed class Fnv1aConsistentHashAlgorithm : IConsistentHashAlgorithm
{
    private const uint FnvOffsetBasis = 2166136261u;
    private const uint FnvPrime = 16777619u;

    /// <summary>
    /// Singleton default instance of <see cref="Fnv1aConsistentHashAlgorithm"/>.
    /// </summary>
    public static readonly Fnv1aConsistentHashAlgorithm Instance = new();

    public uint ComputeHash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var hash = FnvOffsetBasis;
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(key.Length);

        if (maxByteCount <= 256)
        {
            Span<byte> utf8Bytes = stackalloc byte[256];
            var bytesWritten = Encoding.UTF8.GetBytes(key, utf8Bytes);
            var slice = utf8Bytes[..bytesWritten];
            for (var i = 0; i < slice.Length; i++)
            {
                hash = (hash ^ slice[i]) * FnvPrime;
            }
            return hash;
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(key);
            for (var i = 0; i < bytes.Length; i++)
            {
                hash = (hash ^ bytes[i]) * FnvPrime;
            }
            return hash;
        }
    }
}
