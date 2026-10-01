using System.Text;
using Centra.Providers.Flotilla.Protocol;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaCrc32Tests
{
    [Fact]
    public void Calculate_StandardCrc32TestVector_MatchesExpected()
    {
        // Standard IEEE 802.3 CRC32 of "123456789" is 0xCBF43926
        var bytes = Encoding.ASCII.GetBytes("123456789");
        var crc = FlotillaCrc32.Calculate(bytes);
        crc.ShouldBe(0xCBF43926u);
    }

    [Fact]
    public void Calculate_EmptySpan_ReturnsZero()
    {
        var crc = FlotillaCrc32.Calculate(ReadOnlySpan<byte>.Empty);
        crc.ShouldBe(0x00000000u);
    }
}
