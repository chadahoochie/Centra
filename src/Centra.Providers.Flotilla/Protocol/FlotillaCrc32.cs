namespace Centra.Providers.Flotilla.Protocol;

/// <summary>
/// IEEE 802.3 CRC32 checksum calculator for Flotilla frame verification.
/// </summary>
public static class FlotillaCrc32
{
    /// <summary>
    /// Computes the IEEE 802.3 CRC32 checksum for the given byte span.
    /// </summary>
    public static uint Calculate(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                var mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320 & mask);
            }
        }
        return ~crc;
    }
}
