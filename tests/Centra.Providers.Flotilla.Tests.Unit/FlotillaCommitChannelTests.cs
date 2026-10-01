using Centra.Providers.Flotilla.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaCommitChannelTests
{
    [Fact]
    public async Task WriteCommitAsync_StreamsToReader()
    {
        var channel = new FlotillaCommitChannel(10);
        var entry = new CommittedEntry
        {
            LogIndex = 42,
            Term = 3,
            Data = new byte[] { 1, 2, 3 }
        };

        await channel.WriteCommitAsync(entry);
        channel.Complete();

        var list = new List<CommittedEntry>();
        await foreach (var item in channel.ReadCommitsAsync())
        {
            list.Add(item);
        }

        list.Count.ShouldBe(1);
        list[0].LogIndex.ShouldBe(42UL);
        list[0].Term.ShouldBe(3UL);
        list[0].Data.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
    }
}
