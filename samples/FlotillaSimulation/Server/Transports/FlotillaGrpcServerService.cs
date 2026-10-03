using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Providers.Flotilla.Grpc;
using Centra.Sample.FlotillaSimulation.Server.Consensus;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.FlotillaSimulation.Server.Transports;

/// <summary>
/// gRPC service implementation for Flotilla consensus server handling proposals and commit streaming.
/// </summary>
public sealed class FlotillaGrpcServerService : FlotillaService.FlotillaServiceBase
{
    private readonly FlotillaConsensusEngine _engine;
    private readonly FlotillaConsensusMetrics _metrics;
    private readonly ILogger<FlotillaGrpcServerService> _logger;

    public FlotillaGrpcServerService(
        FlotillaConsensusEngine engine,
        FlotillaConsensusMetrics metrics,
        ILogger<FlotillaGrpcServerService> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task<ProposalResponse> Propose(ProposalRequest request, ServerCallContext context)
    {
        var sw = Stopwatch.StartNew();

        ActivityContext parentContext = default;
        var traceParent = context.RequestHeaders.GetValue("traceparent");
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            var traceState = context.RequestHeaders.GetValue("tracestate");
            ActivityContext.TryParse(traceParent, traceState, out parentContext);
        }

        using var activity = CentraDiagnostics.StartFlotillaServerProposeActivity("grpc", parentContext);

        var (success, index, term, leaderId) = _engine.Propose(request.Payload.Span);
        sw.Stop();
        _metrics.RecordProposal("grpc", success, sw.Elapsed.TotalMilliseconds);

        return Task.FromResult(new ProposalResponse
        {
            Success = success,
            Index = index,
            Term = term,
            LeaderId = leaderId,
            ErrorMessage = string.Empty
        });
    }

    public override async Task SubscribeCommits(
        CommitSubscribeRequest request,
        IServerStreamWriter<CommitEntryProto> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("New gRPC commit subscriber connected from {Peer}", context.Peer);

        try
        {
            await foreach (var commit in _engine.SubscribeAsync(context.CancellationToken).ConfigureAwait(false))
            {
                var proto = new CommitEntryProto
                {
                    Index = commit.LogIndex,
                    Term = commit.Term,
                    Data = ByteString.CopyFrom(commit.Data.Span)
                };

                await responseStream.WriteAsync(proto, context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("gRPC commit subscriber disconnected due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "gRPC subscriber {Peer} disconnected", context.Peer);
        }

        _logger.LogInformation("gRPC subscriber {Peer} disconnected", context.Peer);
    }
}
