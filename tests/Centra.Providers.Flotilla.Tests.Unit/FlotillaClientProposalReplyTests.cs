using Centra.Providers.Flotilla.Tcp.Protocol;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaClientProposalReplyTests
{
    [Fact]
    public void ReplySize_MatchesFlotillaRustSpecification_32Bytes()
    {
        FlotillaClientProposalReply.ReplySize.ShouldBe(32);
    }

    [Fact]
    public void WriteTo_And_ReadFrom_RoundtripsSuccessfully()
    {
        var original = new FlotillaClientProposalReply(
            success: 1,
            index: 1050,
            term: 4,
            leaderId: 2);

        var buffer = new byte[FlotillaClientProposalReply.ReplySize];
        original.WriteTo(buffer);

        var decoded = FlotillaClientProposalReply.ReadFrom(buffer);

        decoded.Success.ShouldBe((byte)1);
        decoded.IsSuccess.ShouldBeTrue();
        decoded.Index.ShouldBe(1050UL);
        decoded.Term.ShouldBe(4UL);
        decoded.LeaderId.ShouldBe(2UL);
    }

    [Fact]
    public void ReadFrom_FailureReply_ReportsIsSuccessFalse()
    {
        var original = new FlotillaClientProposalReply(
            success: 0,
            index: 0,
            term: 5,
            leaderId: 3);

        var buffer = new byte[FlotillaClientProposalReply.ReplySize];
        original.WriteTo(buffer);

        var decoded = FlotillaClientProposalReply.ReadFrom(buffer);
        decoded.IsSuccess.ShouldBeFalse();
        decoded.LeaderId.ShouldBe(3UL);
    }
}
