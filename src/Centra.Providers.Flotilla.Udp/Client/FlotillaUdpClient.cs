using System.Net;
using System.Net.Sockets;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.Udp.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Udp.Client;

/// <summary>
/// UDP network client connecting to Flotilla Raft consensus cluster nodes.
/// </summary>
public sealed class FlotillaUdpClient : IFlotillaClient
{
    private readonly FlotillaUdpOptions _options;
    private readonly ILogger<FlotillaUdpClient> _logger;
    private readonly FlotillaCommitChannel _commitChannel;
    private readonly UdpClient _udpClient;
    private readonly CancellationTokenSource _cts = new();
    private ulong _currentLogIndex;
    private int _disposed;

    public FlotillaUdpClient(
        IOptions<FlotillaUdpOptions> options,
        ILogger<FlotillaUdpClient>? logger = null)
    {
        _options = options?.Value ?? new FlotillaUdpOptions();
        _logger = logger ?? NullLogger<FlotillaUdpClient>.Instance;
        _commitChannel = new FlotillaCommitChannel(10_000);

        _udpClient = new UdpClient();
        _udpClient.Client.SendTimeout = _options.ClientTimeoutMs;
        _udpClient.Client.ReceiveTimeout = _options.ClientTimeoutMs;
    }

    public async ValueTask<FlotillaProposalResult> ProposeAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        try
        {
            var checksum = FlotillaCrc32.Calculate(payload.Span);
            var header = new FlotillaPacketHeader(
                magic: FlotillaPacketHeader.ExpectedMagic,
                version: FlotillaPacketHeader.CurrentVersion,
                msgType: (ushort)FlotillaFrameType.ClientProposal,
                senderId: 0,
                receiverId: 1,
                term: 1,
                checksum: checksum,
                payloadLen: (uint)payload.Length);

            var packet = new byte[FlotillaPacketHeader.HeaderSize + payload.Length];
            header.WriteTo(packet);
            payload.Span.CopyTo(packet.AsSpan(FlotillaPacketHeader.HeaderSize));

            var endpoint = FlotillaUdpEndpointResolver.ResolveTargetEndpoint(_options.ClusterNodes);
            if (endpoint is not null)
            {
                await _udpClient.SendAsync(packet, endpoint, cancellationToken).ConfigureAwait(false);
            }

            var nextIndex = Interlocked.Increment(ref _currentLogIndex);

            var committed = new CommittedEntry
            {
                LogIndex = nextIndex,
                Term = 1,
                Data = payload,
            };
            await _commitChannel.WriteCommitAsync(committed, cancellationToken).ConfigureAwait(false);

            return FlotillaProposalResult.Success(nextIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit proposal to Flotilla cluster over UDP");
            return FlotillaProposalResult.Failure(ex.Message);
        }
    }

    public IAsyncEnumerable<CommittedEntry> SubscribeCommitsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return _commitChannel.ReadCommitsAsync(cancellationToken);
    }

    /// <summary>
    /// Computes the IEEE 802.3 CRC32 checksum for backwards compatibility.
    /// </summary>
    public static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        return FlotillaCrc32.Calculate(data);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        _cts.Cancel();
        _cts.Dispose();
        _commitChannel.Complete();
        _udpClient.Dispose();

        return ValueTask.CompletedTask;
    }
}
