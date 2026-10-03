using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Centra.Diagnostics;
using Centra.Providers.Flotilla.Protocol;
using Centra.Sample.FlotillaSimulation.Server.Consensus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.FlotillaSimulation.Server.Transports;

/// <summary>
/// UDP transport listener for Flotilla consensus simulator handling binary client datagrams and commit broadcasts.
/// </summary>
public sealed class FlotillaUdpServer : BackgroundService
{
    private readonly FlotillaConsensusEngine _engine;
    private readonly FlotillaConsensusMetrics _metrics;
    private readonly IConfiguration _config;
    private readonly ILogger<FlotillaUdpServer> _logger;
    private readonly UdpClient _udpServer;
    private readonly ConcurrentDictionary<IPEndPoint, DateTimeOffset> _subscribers = new();
    private readonly int _port;

    public int Port => _port;

    public FlotillaUdpServer(
        FlotillaConsensusEngine engine,
        FlotillaConsensusMetrics metrics,
        IConfiguration config,
        ILogger<FlotillaUdpServer> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var configuredPort = _config.GetValue("Flotilla:UdpPort", 9200);
        _udpServer = new UdpClient(new IPEndPoint(IPAddress.Any, configuredPort));
        _port = ((IPEndPoint)_udpServer.Client.LocalEndPoint!).Port;
    }

    public override void Dispose()
    {
        _udpServer.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Flotilla UDP Consensus Server listening on port {Port}", _port);

        var broadcastTask = Task.Run(() => BroadcastCommitsLoopAsync(_udpServer, stoppingToken), stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await _udpServer.ReceiveAsync(stoppingToken).ConfigureAwait(false);
                _subscribers[result.RemoteEndPoint] = DateTimeOffset.UtcNow;

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

                if (header.MsgType == (ushort)FlotillaFrameType.ClientProposal && buffer.Length >= FlotillaPacketHeader.HeaderSize + (int)header.PayloadLen)
                {
                    var sw = Stopwatch.StartNew();
                    var payloadMemory = result.Buffer.AsMemory(FlotillaPacketHeader.HeaderSize, (int)header.PayloadLen);

                    ActivityContext parentContext = default;
                    if (FlotillaTraceEnvelope.IsEnveloped(payloadMemory.Span))
                    {
                        var unwrap = FlotillaTraceEnvelope.Unwrap(payloadMemory);
                        parentContext = unwrap.Context;
                    }

                    using var activity = CentraDiagnostics.StartFlotillaServerProposeActivity("udp", parentContext);

                    _engine.Propose(payloadMemory.Span);
                    sw.Stop();
                    _metrics.RecordProposal("udp", true, sw.Elapsed.TotalMilliseconds);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Flotilla UDP listener stopping requested");
        }
        finally
        {
            _logger.LogInformation("Flotilla UDP Consensus Server stopped");
            await broadcastTask.ConfigureAwait(false);
        }
    }

    internal async Task BroadcastCommitsLoopAsync(UdpClient udpServer, CancellationToken ct)
    {
        try
        {
            await foreach (var commit in _engine.SubscribeAsync(ct).ConfigureAwait(false))
            {
                var payloadLen = 16 + commit.Data.Length;
                var frameBuf = new byte[FlotillaPacketHeader.HeaderSize + payloadLen];

                var payloadSpan = frameBuf.AsSpan(FlotillaPacketHeader.HeaderSize);
                BinaryPrimitives.WriteUInt64LittleEndian(payloadSpan[..8], commit.LogIndex);
                BinaryPrimitives.WriteUInt64LittleEndian(payloadSpan.Slice(8, 8), commit.Term);
                commit.Data.Span.CopyTo(payloadSpan[16..]);

                var header = new FlotillaPacketHeader(
                    magic: FlotillaPacketHeader.ExpectedMagic,
                    version: FlotillaPacketHeader.CurrentVersion,
                    msgType: (ushort)FlotillaFrameType.AppendEntriesArgs,
                    senderId: _engine.LeaderId,
                    receiverId: 0,
                    term: commit.Term,
                    checksum: FlotillaCrc32.Calculate(payloadSpan),
                    payloadLen: (uint)payloadLen);

                header.WriteTo(frameBuf);

                foreach (var (ep, _) in _subscribers)
                {
                    try
                    {
                        await udpServer.SendAsync(frameBuf, ep, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to send UDP datagram to subscriber {EndPoint}", ep);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("UDP broadcast loop canceled");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UDP broadcast loop completed");
        }
    }
}
