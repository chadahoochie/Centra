using System.Text;
using Centra.Providers.Flotilla.Grpc;
using Centra.Providers.Flotilla.Grpc.Client;
using Centra.Providers.Flotilla.Grpc.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaGrpcClientTests
{
    [Fact]
    public async Task ProposeAsync_WhenServerReturnsSuccess_ReturnsSuccessAndStreamsCommits()
    {
        var mockHandler = new MockFlotillaGrpcHttpHandler
        {
            ResponseToReturn = new ProposalResponse
            {
                Success = true,
                Index = 120,
                Term = 3,
                LeaderId = 1,
            }
        };

        var options = Microsoft.Extensions.Options.Options.Create(new FlotillaGrpcOptions
        {
            ClusterNodes = ["http://127.0.0.1:9001"],
            ClientTimeoutMs = 1000
        });

        await using var client = new FlotillaGrpcClient(options, httpHandler: mockHandler);
        var payload = Encoding.UTF8.GetBytes("grpc_test_payload");

        var result = await client.ProposeAsync(payload);

        result.IsSuccess.ShouldBeTrue();
        result.LogIndex.ShouldBe(120UL);
        mockHandler.CapturedPayload.ShouldNotBeNull();
        Encoding.UTF8.GetString(mockHandler.CapturedPayload).ShouldBe("grpc_test_payload");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var commits = new List<ulong>();
        await foreach (var commit in client.SubscribeCommitsAsync(cts.Token))
        {
            commits.Add(commit.LogIndex);
            break;
        }

        commits.Count.ShouldBe(1);
        commits[0].ShouldBe(120UL);
    }

    [Fact]
    public async Task ProposeAsync_WhenServerReturnsFailure_ReturnsFailureWithErrorMessage()
    {
        var mockHandler = new MockFlotillaGrpcHttpHandler
        {
            ResponseToReturn = new ProposalResponse
            {
                Success = false,
                Index = 0,
                Term = 3,
                LeaderId = 2,
                ErrorMessage = "Node is in Candidate state",
            }
        };

        var options = Microsoft.Extensions.Options.Options.Create(new FlotillaGrpcOptions
        {
            ClusterNodes = ["http://127.0.0.1:9001"],
            ClientTimeoutMs = 1000
        });

        await using var client = new FlotillaGrpcClient(options, httpHandler: mockHandler);
        var result = await client.ProposeAsync(new byte[] { 1, 2, 3 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Node is in Candidate state");
    }

    [Fact]
    public void EndpointResolver_ParsesVariousUriFormats()
    {
        var uri1 = FlotillaGrpcEndpointResolver.ResolveTargetUri(["http://127.0.0.1:9005"]);
        uri1.ShouldBe(new Uri("http://127.0.0.1:9005"));

        var uri2 = FlotillaGrpcEndpointResolver.ResolveTargetUri(["127.0.0.1:9005"]);
        uri2.ShouldBe(new Uri("http://127.0.0.1:9005"));

        var uri3 = FlotillaGrpcEndpointResolver.ResolveTargetUri([]);
        uri3.ShouldBe(new Uri("http://127.0.0.1:9001"));
    }
}
