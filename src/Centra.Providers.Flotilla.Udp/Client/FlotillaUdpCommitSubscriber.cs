using System.Buffers.Binary;
using System.Net.Sockets;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.Udp.Options;
using Microsoft.Extensions.Logging;

namespace Centra.Providers.Flotilla.Udp.Client;

internal sealed class FlotillaUdpCommitSubscriber : IAsyncDisposable
{
    private readonly UdpClient _udpClient;
    private readonly FlotillaUdpOptions _options;
    private readonly FlotillaCommitChannel _commitChannel;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;

    public FlotillaUdpCommitSubscriber(
        UdpClient udpClient,
        FlotillaUdpOptions options,
        FlotillaCommitChannel commitChannel,
        ILogger logger)
    {
        _udpClient = udpClient ?? throw new ArgumentNullException(nameof(udpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _commitChannel = commitChannel ?? throw new ArgumentNullException(nameof(commitChannel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        _receiveTask = Task.Run(RunReceiveAsync);
    }

    internal async Task RunReceiveAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(_cts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                if (buffer.Length < FlotillaPacketHeader.HeaderSize)
                {
                    continue;
                }

                var header = FlotillaPacketHeader.ReadFrom(buffer);
                if (header.Magic != FlotillaPacketHeader.ExpectedMagic)
                {
                    continue;
                }

                if (header.MsgType == (ushort)FlotillaFrameType.AppendEntriesArgs && buffer.Length >= FlotillaPacketHeader.HeaderSize + 16)
                {
                    var payloadSpan = buffer.AsSpan(FlotillaPacketHeader.HeaderSize);
                    var logIndex = BinaryPrimitives.ReadUInt64LittleEndian(payloadSpan[..8]);
                    var term = BinaryPrimitives.ReadUInt64LittleEndian(payloadSpan.Slice(8, 8));
                    var data = buffer.AsMemory(FlotillaPacketHeader.HeaderSize + 16);

                    var entry = new CommittedEntry
                    {
                        LogIndex = logIndex,
                        Term = term,
                        Data = data
                    };

                    await _commitChannel.WriteCommitAsync(entry, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UDP commit subscriber received packet decode error");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
        _cts.Dispose();
    }
}
