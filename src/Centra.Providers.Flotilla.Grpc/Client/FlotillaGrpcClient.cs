using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Grpc.Options;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Grpc.Client;

/// <summary>
/// HTTP/2 gRPC client connecting to Flotilla Raft consensus cluster nodes.
/// </summary>
public sealed class FlotillaGrpcClient : IFlotillaClient
{
    private readonly FlotillaGrpcOptions _options;
    private readonly ILogger<FlotillaGrpcClient> _logger;
    private readonly FlotillaCommitChannel _commitChannel;
    private readonly GrpcChannel _channel;
    private readonly FlotillaService.FlotillaServiceClient _client;
    private int _disposed;

    public FlotillaGrpcClient(
        IOptions<FlotillaGrpcOptions> options,
        ILogger<FlotillaGrpcClient>? logger = null,
        HttpMessageHandler? httpHandler = null)
    {
        _options = options?.Value ?? new FlotillaGrpcOptions();
        _logger = logger ?? NullLogger<FlotillaGrpcClient>.Instance;
        _commitChannel = new FlotillaCommitChannel(10_000);

        var targetUri = FlotillaGrpcEndpointResolver.ResolveTargetUri(_options.ClusterNodes);
        var channelOptions = new GrpcChannelOptions();
        if (httpHandler is not null)
        {
            channelOptions.HttpHandler = httpHandler;
        }

        _channel = GrpcChannel.ForAddress(targetUri, channelOptions);
        _client = new FlotillaService.FlotillaServiceClient(_channel);
    }

    public async ValueTask<FlotillaProposalResult> ProposeAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ClientTimeoutMs);

        try
        {
            var req = new ProposalRequest
            {
                Payload = ByteString.CopyFrom(payload.Span)
            };

            var response = await _client.ProposeAsync(req, cancellationToken: timeoutCts.Token).ResponseAsync.ConfigureAwait(false);

            if (response.Success)
            {
                var committed = new CommittedEntry
                {
                    LogIndex = response.Index,
                    Term = response.Term,
                    Data = payload,
                };
                await _commitChannel.WriteCommitAsync(committed, cancellationToken).ConfigureAwait(false);

                return FlotillaProposalResult.Success(response.Index);
            }

            var errorMsg = !string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? response.ErrorMessage
                : (response.LeaderId != 0
                    ? $"Proposal rejected; current leader is node {response.LeaderId}"
                    : "Proposal rejected by Flotilla cluster");

            return FlotillaProposalResult.Failure(errorMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit proposal to Flotilla cluster over gRPC");
            return FlotillaProposalResult.Failure(ex.Message);
        }
    }

    public IAsyncEnumerable<CommittedEntry> SubscribeCommitsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return _commitChannel.ReadCommitsAsync(cancellationToken);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        _commitChannel.Complete();
        _channel.Dispose();

        return ValueTask.CompletedTask;
    }
}
