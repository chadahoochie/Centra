using System.Buffers.Binary;
using System.Net.Sockets;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.Tcp.Options;
using Microsoft.Extensions.Logging;

namespace Centra.Providers.Flotilla.Tcp.Client;

internal sealed class FlotillaTcpCommitSubscriber : IAsyncDisposable
{
    private readonly FlotillaTcpOptions _options;
    private readonly FlotillaCommitChannel _commitChannel;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _streamTask;

    public FlotillaTcpCommitSubscriber(
        FlotillaTcpOptions options,
        FlotillaCommitChannel commitChannel,
        ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _commitChannel = commitChannel ?? throw new ArgumentNullException(nameof(commitChannel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        _streamTask = Task.Run(RunStreamAsync);
    }

    internal async Task RunStreamAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var endpoint = FlotillaTcpEndpointResolver.ResolveTargetEndpoint(_options.ClusterNodes);
                if (endpoint is null)
                {
                    await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                using var client = new System.Net.Sockets.TcpClient();
                client.NoDelay = true;
                await client.ConnectAsync(endpoint, _cts.Token).ConfigureAwait(false);
                using var stream = client.GetStream();

                var handshakeHeader = new FlotillaPacketHeader(
                    magic: FlotillaPacketHeader.ExpectedMagic,
                    version: FlotillaPacketHeader.CurrentVersion,
                    msgType: (ushort)FlotillaFrameType.HeartbeatArgs,
                    senderId: 0,
                    receiverId: 1,
                    term: 1,
                    checksum: 0,
                    payloadLen: 0);

                var handshakeBuf = new byte[FlotillaPacketHeader.HeaderSize];
                handshakeHeader.WriteTo(handshakeBuf);
                await stream.WriteAsync(handshakeBuf, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);

                var headerBuf = new byte[FlotillaPacketHeader.HeaderSize];
                while (!_cts.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(headerBuf, _cts.Token).ConfigureAwait(false);
                    var header = FlotillaPacketHeader.ReadFrom(headerBuf);

                    if (header.Magic != FlotillaPacketHeader.ExpectedMagic)
                    {
                        break;
                    }

                    var payloadBuf = new byte[header.PayloadLen];
                    if (header.PayloadLen > 0)
                    {
                        await stream.ReadExactlyAsync(payloadBuf, _cts.Token).ConfigureAwait(false);
                    }

                    if (header.MsgType == (ushort)FlotillaFrameType.AppendEntriesArgs && payloadBuf.Length >= 16)
                    {
                        var logIndex = BinaryPrimitives.ReadUInt64LittleEndian(payloadBuf.AsSpan(0, 8));
                        var term = BinaryPrimitives.ReadUInt64LittleEndian(payloadBuf.AsSpan(8, 8));
                        var data = payloadBuf.AsMemory(16);

                        var entry = new CommittedEntry
                        {
                            LogIndex = logIndex,
                            Term = term,
                            Data = data
                        };
                        await _commitChannel.WriteCommitAsync(entry, _cts.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "TCP commit subscriber connection disconnected or unavailable; retrying...");
                try
                {
                    await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_streamTask is not null)
        {
            try
            {
                await _streamTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
        _cts.Dispose();
    }
}
