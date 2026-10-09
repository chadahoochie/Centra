using System.Net;
using System.Net.Sockets;
using System.Text;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.Tcp.Client;
using Centra.Providers.Flotilla.Tcp.Options;
using Centra.Providers.Flotilla.Tcp.Protocol;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaTcpClientTests
{
    [Fact]
    public async Task ProposeAsync_WhenServerReturnsSuccess_ReturnsSuccessAndAppendsToCommitStream()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var proposalHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openSockets = new List<Socket>();

        var serverTask = Task.Run(async () =>
        {
            try
            {
                while (!serverCts.IsCancellationRequested && !proposalHandled.Task.IsCompleted)
                {
                    var socket = await listener.AcceptSocketAsync(serverCts.Token);
                    lock (openSockets)
                    {
                        openSockets.Add(socket);
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var stream = new NetworkStream(socket, ownsSocket: false);
                            while (!serverCts.IsCancellationRequested && !proposalHandled.Task.IsCompleted)
                            {
                                var headerBuf = new byte[FlotillaPacketHeader.HeaderSize];
                                await stream.ReadExactlyAsync(headerBuf, serverCts.Token);
                                var header = FlotillaPacketHeader.ReadFrom(headerBuf);

                                var payloadBuf = new byte[header.PayloadLen];
                                if (header.PayloadLen > 0)
                                {
                                    await stream.ReadExactlyAsync(payloadBuf, serverCts.Token);
                                }

                                if (header.MsgType == (ushort)FlotillaFrameType.HeartbeatArgs)
                                {
                                    // Subscriber connection handshake; maintain connection
                                    continue;
                                }

                                if (header.MsgType == (ushort)FlotillaFrameType.ClientProposal)
                                {
                                    // Send reply: success = 1, index = 42, term = 1, leader_id = 1
                                    var reply = new FlotillaClientProposalReply(1, 42, 1, 1);
                                    var replyPayload = new byte[FlotillaClientProposalReply.ReplySize];
                                    reply.WriteTo(replyPayload);

                                    var replyHeader = new FlotillaPacketHeader(
                                        magic: FlotillaPacketHeader.ExpectedMagic,
                                        version: FlotillaPacketHeader.CurrentVersion,
                                        msgType: (ushort)FlotillaFrameType.ClientProposalReply,
                                        senderId: 1,
                                        receiverId: 0,
                                        term: 1,
                                        checksum: FlotillaCrc32.Calculate(replyPayload),
                                        payloadLen: (uint)replyPayload.Length);

                                    var responsePacket = new byte[FlotillaPacketHeader.HeaderSize + replyPayload.Length];
                                    replyHeader.WriteTo(responsePacket);
                                    replyPayload.CopyTo(responsePacket.AsSpan(FlotillaPacketHeader.HeaderSize));

                                    await stream.WriteAsync(responsePacket, serverCts.Token);
                                    await stream.FlushAsync(serverCts.Token);
                                    proposalHandled.TrySetResult();
                                    break;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }, serverCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
        });

        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new FlotillaTcpOptions
            {
                ClusterNodes = [$"127.0.0.1:{port}"],
                ClientTimeoutMs = 2000,
                EnableChecksumVerification = true,
            });

            await using var client = new FlotillaTcpClient(options);
            var payload = Encoding.UTF8.GetBytes("sample_order");

            var result = await client.ProposeAsync(payload);
            result.IsSuccess.ShouldBeTrue();
            result.LogIndex.ShouldBe(42UL);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var commits = new List<ulong>();
            await foreach (var commit in client.SubscribeCommitsAsync(cts.Token))
            {
                commits.Add(commit.LogIndex);
                break;
            }

            commits.Count.ShouldBe(1);
            commits[0].ShouldBe(42UL);

            await proposalHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            serverCts.Cancel();
            listener.Stop();
            try { await serverTask; } catch { }
            lock (openSockets)
            {
                foreach (var s in openSockets)
                {
                    try { s.Dispose(); } catch { }
                }
            }
        }
    }

    [Fact]
    public async Task ProposeAsync_WhenServerReturnsFollowerRejection_ReturnsFailureWithLeaderInfo()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var proposalHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openSockets = new List<Socket>();

        var serverTask = Task.Run(async () =>
        {
            try
            {
                while (!serverCts.IsCancellationRequested && !proposalHandled.Task.IsCompleted)
                {
                    var socket = await listener.AcceptSocketAsync(serverCts.Token);
                    lock (openSockets)
                    {
                        openSockets.Add(socket);
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var stream = new NetworkStream(socket, ownsSocket: false);
                            while (!serverCts.IsCancellationRequested && !proposalHandled.Task.IsCompleted)
                            {
                                var headerBuf = new byte[FlotillaPacketHeader.HeaderSize];
                                await stream.ReadExactlyAsync(headerBuf, serverCts.Token);
                                var header = FlotillaPacketHeader.ReadFrom(headerBuf);

                                var payloadBuf = new byte[header.PayloadLen];
                                if (header.PayloadLen > 0)
                                {
                                    await stream.ReadExactlyAsync(payloadBuf, serverCts.Token);
                                }

                                if (header.MsgType == (ushort)FlotillaFrameType.HeartbeatArgs)
                                {
                                    // Subscriber connection handshake; maintain connection
                                    continue;
                                }

                                if (header.MsgType == (ushort)FlotillaFrameType.ClientProposal)
                                {
                                    // Send rejection: success = 0, index = 0, term = 2, leader_id = 99
                                    var reply = new FlotillaClientProposalReply(0, 0, 2, 99);
                                    var replyPayload = new byte[FlotillaClientProposalReply.ReplySize];
                                    reply.WriteTo(replyPayload);

                                    var replyHeader = new FlotillaPacketHeader(
                                        magic: FlotillaPacketHeader.ExpectedMagic,
                                        version: FlotillaPacketHeader.CurrentVersion,
                                        msgType: (ushort)FlotillaFrameType.ClientProposalReply,
                                        senderId: 2,
                                        receiverId: 0,
                                        term: 2,
                                        checksum: FlotillaCrc32.Calculate(replyPayload),
                                        payloadLen: (uint)replyPayload.Length);

                                    var responsePacket = new byte[FlotillaPacketHeader.HeaderSize + replyPayload.Length];
                                    replyHeader.WriteTo(responsePacket);
                                    replyPayload.CopyTo(responsePacket.AsSpan(FlotillaPacketHeader.HeaderSize));

                                    await stream.WriteAsync(responsePacket, serverCts.Token);
                                    await stream.FlushAsync(serverCts.Token);
                                    proposalHandled.TrySetResult();
                                    break;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }, serverCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
        });

        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new FlotillaTcpOptions
            {
                ClusterNodes = [$"127.0.0.1:{port}"],
                ClientTimeoutMs = 2000,
            });

            await using var client = new FlotillaTcpClient(options);
            var result = await client.ProposeAsync(new byte[] { 1, 2, 3 });

            result.IsSuccess.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage.ShouldContain("leader is node 99");

            await proposalHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            serverCts.Cancel();
            listener.Stop();
            try { await serverTask; } catch { }
            lock (openSockets)
            {
                foreach (var s in openSockets)
                {
                    try { s.Dispose(); } catch { }
                }
            }
        }
    }

    [Fact]
    public async Task ProposeAsync_WhenConnectionFails_ReturnsFailureGracefully()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new FlotillaTcpOptions
        {
            // Port 1 is closed
            ClusterNodes = ["127.0.0.1:1"],
            ClientTimeoutMs = 100,
        });

        await using var client = new FlotillaTcpClient(options);
        var result = await client.ProposeAsync(new byte[] { 1, 2, 3 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
    }
}
