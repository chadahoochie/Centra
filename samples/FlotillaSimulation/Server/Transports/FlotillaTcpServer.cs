using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Centra.Diagnostics;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.Tcp.Protocol;
using Centra.Sample.FlotillaSimulation.Server.Consensus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.FlotillaSimulation.Server.Transports;

/// <summary>
/// TCP transport listener for Flotilla consensus simulator handling binary client proposals and subscriber streams.
/// </summary>
public sealed class FlotillaTcpServer : BackgroundService
{
    private readonly FlotillaConsensusEngine _engine;
    private readonly FlotillaConsensusMetrics _metrics;
    private readonly IConfiguration _config;
    private readonly ILogger<FlotillaTcpServer> _logger;
    private readonly int _port;

    public FlotillaTcpServer(
        FlotillaConsensusEngine engine,
        FlotillaConsensusMetrics metrics,
        IConfiguration config,
        ILogger<FlotillaTcpServer> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _port = _config.GetValue("Flotilla:TcpPort", 9100);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Server.NoDelay = true;
        listener.Start();

        _logger.LogInformation("Flotilla TCP Consensus Server listening on port {Port}", _port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Flotilla TCP listener stopping requested");
        }
        finally
        {
            listener.Stop();
            _logger.LogInformation("Flotilla TCP Consensus Server stopped");
        }
    }

    internal async Task HandleClientAsync(System.Net.Sockets.TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var headerBuf = new byte[FlotillaPacketHeader.HeaderSize];

            while (!ct.IsCancellationRequested && client.Connected)
            {
                try
                {
                    await stream.ReadExactlyAsync(headerBuf, ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                var header = FlotillaPacketHeader.ReadFrom(headerBuf);
                if (header.Magic != FlotillaPacketHeader.ExpectedMagic)
                {
                    break;
                }

                if (header.MsgType == (ushort)FlotillaFrameType.HeartbeatArgs)
                {
                    // Subscriber connection: stream committed log entries to this client
                    await StreamCommitsToClientAsync(stream, ct).ConfigureAwait(false);
                    break;
                }

                if (header.MsgType == (ushort)FlotillaFrameType.ClientProposal)
                {
                    var sw = Stopwatch.StartNew();
                    var payloadBuf = new byte[header.PayloadLen];
                    if (header.PayloadLen > 0)
                    {
                        await stream.ReadExactlyAsync(payloadBuf, ct).ConfigureAwait(false);
                    }

                    ActivityContext parentContext = default;
                    ReadOnlyMemory<byte> rawPayload = payloadBuf;
                    if (FlotillaTraceEnvelope.IsEnveloped(payloadBuf))
                    {
                        var unwrap = FlotillaTraceEnvelope.Unwrap(payloadBuf);
                        parentContext = unwrap.Context;
                        rawPayload = unwrap.Payload;
                    }

                    using var activity = CentraDiagnostics.StartFlotillaServerProposeActivity("tcp", parentContext);

                    var serverContext = (activity != null && activity.Context != default) ? activity.Context : parentContext;
                    byte[] payloadToPropose;
                    if (serverContext != default)
                    {
                        payloadToPropose = FlotillaTraceEnvelope.Wrap(serverContext, rawPayload.Span);
                    }
                    else
                    {
                        payloadToPropose = payloadBuf;
                    }

                    var (success, index, term, leaderId) = _engine.Propose(payloadToPropose);
                    sw.Stop();
                    _metrics.RecordProposal("tcp", success, sw.Elapsed.TotalMilliseconds);

                    var reply = new FlotillaClientProposalReply((byte)(success ? 1 : 0), index, term, leaderId);
                    var replyPayload = new byte[FlotillaClientProposalReply.ReplySize];
                    reply.WriteTo(replyPayload);

                    var replyHeader = new FlotillaPacketHeader(
                        magic: FlotillaPacketHeader.ExpectedMagic,
                        version: FlotillaPacketHeader.CurrentVersion,
                        msgType: (ushort)FlotillaFrameType.ClientProposalReply,
                        senderId: leaderId,
                        receiverId: header.SenderId,
                        term: term,
                        checksum: FlotillaCrc32.Calculate(replyPayload),
                        payloadLen: (uint)replyPayload.Length);

                    var responsePacket = new byte[FlotillaPacketHeader.HeaderSize + replyPayload.Length];
                    replyHeader.WriteTo(responsePacket);
                    replyPayload.CopyTo(responsePacket.AsSpan(FlotillaPacketHeader.HeaderSize));

                    await stream.WriteAsync(responsePacket, ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                }
            }
        }
    }

    internal async Task StreamCommitsToClientAsync(NetworkStream stream, CancellationToken ct)
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

                await stream.WriteAsync(frameBuf, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Client TCP subscription stream canceled");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Client TCP subscription stream finished");
        }
    }
}
