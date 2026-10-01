using System.Net.Sockets;
using Centra.Providers.Flotilla.Client;
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
    }

    public async ValueTask<FlotillaProposalResult> ProposeAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ClientTimeoutMs);

        await _gate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(timeoutCts.Token).ConfigureAwait(false);

            var packet = FlotillaTcpFrameCodec.EncodeProposalFrame(payload.Span);
            await _stream!.WriteAsync(packet, timeoutCts.Token).ConfigureAwait(false);
            await _stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            var (_, reply) = await FlotillaTcpFrameCodec.ReadReplyFrameAsync(
                _stream,
                _options.EnableChecksumVerification,
                timeoutCts.Token).ConfigureAwait(false);

            if (reply.IsSuccess)
            {
                var committed = new CommittedEntry
                {
                    LogIndex = reply.Index,
                    Term = reply.Term,
                    Data = payload,
                };
                await _commitChannel.WriteCommitAsync(committed, cancellationToken).ConfigureAwait(false);

                return FlotillaProposalResult.Success(reply.Index);
            }

            var leaderMsg = reply.LeaderId != 0
                ? $"Proposal rejected; current leader is node {reply.LeaderId}"
                : "Proposal rejected by Flotilla cluster";

            return FlotillaProposalResult.Failure(leaderMsg);
        }
        catch (Exception ex)
        {
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

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        _commitChannel.Complete();
        CloseConnection();
        _gate.Dispose();

        return ValueTask.CompletedTask;
    }
}
