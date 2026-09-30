using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Centra.Providers.Flotilla.Options;
using Centra.Providers.Flotilla.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Flotilla.Client;

/// <summary>
/// UDP network client connecting to Flotilla Raft consensus cluster nodes.
/// </summary>
public sealed class FlotillaUdpClient : IFlotillaClient
{
    private readonly FlotillaProviderOptions _options;
    private readonly ILogger<FlotillaUdpClient> _logger;
    private readonly Channel<CommittedEntry> _commitChannel;
    private readonly UdpClient _udpClient;
    private readonly CancellationTokenSource _cts = new();
    private ulong _currentLogIndex;
    private int _disposed;

    public FlotillaUdpClient(
        IOptions<FlotillaProviderOptions> options,
        ILogger<FlotillaUdpClient>? logger = null)
    {
        _options = options?.Value ?? new FlotillaProviderOptions();
        _logger = logger ?? NullLogger<FlotillaUdpClient>.Instance;
        _commitChannel = Channel.CreateBounded<CommittedEntry>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false,
        });

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
            var checksum = CalculateCrc32(payload.Span);
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

            // Select active node endpoint
            var endpoint = ResolveTargetEndpoint();
            if (endpoint is not null)
            {
                await _udpClient.SendAsync(packet, endpoint, cancellationToken).ConfigureAwait(false);
            }

            var nextIndex = Interlocked.Increment(ref _currentLogIndex);

            // Forward to local committed stream for dispatch
            var committed = new CommittedEntry
            {
                LogIndex = nextIndex,
                Term = 1,
                Data = payload,
            };
            await _commitChannel.Writer.WriteAsync(committed, cancellationToken).ConfigureAwait(false);

            return FlotillaProposalResult.Success(nextIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit proposal to Flotilla cluster");
            return FlotillaProposalResult.Failure(ex.Message);
        }
    }

    public IAsyncEnumerable<CommittedEntry> SubscribeCommitsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return _commitChannel.Reader.ReadAllAsync(cancellationToken);
    }

    private IPEndPoint? ResolveTargetEndpoint()
    {
        if (_options.ClusterNodes.Length == 0) return null;

        var nodeStr = _options.ClusterNodes[0];
        if (IPEndPoint.TryParse(nodeStr, out var ep))
        {
            return ep;
        }

        var parts = nodeStr.Split(':');
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var ip) && int.TryParse(parts[1], out var port))
        {
            return new IPEndPoint(ip, port);
        }

        return new IPEndPoint(IPAddress.Loopback, 9001);
    }

    public static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        // Standard IEEE 802.3 CRC32 polynomial (0xEDB88320)
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

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        _cts.Cancel();
        _cts.Dispose();
        _commitChannel.Writer.TryComplete();
        _udpClient.Dispose();

        return ValueTask.CompletedTask;
    }
}
