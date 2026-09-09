using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Centra.Core.Actors;

/// <summary>
/// MD5-based implementation of <see cref="IConsistentHashAlgorithm"/> with zero heap allocations on short keys.
/// </summary>
public sealed class Md5ConsistentHashAlgorithm : IConsistentHashAlgorithm
{
    /// <summary>
    /// Singleton default instance of <see cref="Md5ConsistentHashAlgorithm"/>.
    /// </summary>
    public static readonly Md5ConsistentHashAlgorithm Instance = new();

    public uint ComputeHash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var maxByteCount = Encoding.UTF8.GetMaxByteCount(key.Length);
        if (maxByteCount <= 256)
        {
            Span<byte> utf8Bytes = stackalloc byte[256];
            var bytesWritten = Encoding.UTF8.GetBytes(key, utf8Bytes);
            Span<byte> hash = stackalloc byte[16];
            MD5.HashData(utf8Bytes[..bytesWritten], hash);
            return BinaryPrimitives.ReadUInt32LittleEndian(hash);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(key);
            Span<byte> hash = stackalloc byte[16];
            MD5.HashData(bytes, hash);
            return BinaryPrimitives.ReadUInt32LittleEndian(hash);
        }
    }
}
