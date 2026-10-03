using System.Diagnostics;
using System.Net.Sockets;
using Centra.Diagnostics;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.Tcp.Options;
using Centra.Providers.Flotilla.Tcp.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Tcp.Client;

/// <summary>
/// Connection-oriented TCP streaming client connecting to Flotilla Raft consensus cluster nodes.
/// </summary>
public sealed class FlotillaTcpClient : IFlotillaClient
{
    private readonly FlotillaTcpOptions _options;
    private readonly ILogger<FlotillaTcpClient> _logger;
    private readonly FlotillaCommitChannel _commitChannel;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly FlotillaTcpCommitSubscriber _subscriber;
    private System.Net.Sockets.TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private int _disposed;

    public FlotillaTcpClient(
        IOptions<FlotillaTcpOptions> options,
        ILogger<FlotillaTcpClient>? logger = null)
    {
        _options = options?.Value ?? new FlotillaTcpOptions();
        _logger = logger ?? NullLogger<FlotillaTcpClient>.Instance;
        _commitChannel = new FlotillaCommitChannel(10_000);
        _subscriber = new FlotillaTcpCommitSubscriber(_options, _commitChannel, _logger);
        _subscriber.Start();
    }

    public async ValueTask<FlotillaProposalResult> ProposeAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ClientTimeoutMs);

        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartFlotillaProposeActivity("tcp");

        await _gate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(timeoutCts.Token).ConfigureAwait(false);

            var currentContext = Activity.Current?.Context ?? default;
            var framePayload = currentContext != default
                ? FlotillaTraceEnvelope.Wrap(currentContext, payload.Span)
                : payload.Span;

            var packet = FlotillaTcpFrameCodec.EncodeProposalFrame(framePayload);
            await _stream!.WriteAsync(packet, timeoutCts.Token).ConfigureAwait(false);
            await _stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            var (_, reply) = await FlotillaTcpFrameCodec.ReadReplyFrameAsync(
                _stream,
                _options.EnableChecksumVerification,
                timeoutCts.Token).ConfigureAwait(false);

            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            if (reply.IsSuccess)
            {
                CentraMeters.RecordFlotillaProposal("tcp", "success", durationMs);
                var committed = new CommittedEntry
                {
                    LogIndex = reply.Index,
                    Term = reply.Term,
                    Data = payload,
                };
                await _commitChannel.WriteCommitAsync(committed, cancellationToken).ConfigureAwait(false);

                return FlotillaProposalResult.Success(reply.Index);
            }

            CentraMeters.RecordFlotillaProposal("tcp", "rejected", durationMs);
            activity?.SetStatus(ActivityStatusCode.Error, "Proposal rejected by Flotilla cluster");

            var leaderMsg = reply.LeaderId != 0
                ? $"Proposal rejected; current leader is node {reply.LeaderId}"
                : "Proposal rejected by Flotilla cluster";

            return FlotillaProposalResult.Failure(leaderMsg);
        }
        catch (Exception ex)
        {
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordFlotillaProposal("tcp", "error", durationMs);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to submit proposal to Flotilla cluster over TCP");
            CloseConnection();
            return FlotillaProposalResult.Failure(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IAsyncEnumerable<CommittedEntry> SubscribeCommitsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return _commitChannel.ReadCommitsAsync(cancellationToken);
    }

    internal async ValueTask EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_tcpClient is not null && _tcpClient.Connected && _stream is not null)
        {
            return;
        }

        CloseConnection();

        var endpoint = FlotillaTcpEndpointResolver.ResolveTargetEndpoint(_options.ClusterNodes);
        if (endpoint is null)
        {
            throw new InvalidOperationException("No valid Flotilla cluster nodes configured for TCP transport.");
        }

        var client = new System.Net.Sockets.TcpClient();
        client.SendTimeout = _options.ClientTimeoutMs;
        client.ReceiveTimeout = _options.ClientTimeoutMs;
        client.NoDelay = true;

        await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

        _tcpClient = client;
        _stream = client.GetStream();
    }

    internal void CloseConnection()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await _subscriber.DisposeAsync().ConfigureAwait(false);
        _commitChannel.Complete();
        CloseConnection();
        _gate.Dispose();
    }
}
