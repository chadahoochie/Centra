using System.Net;
using Centra.Providers.Flotilla.Grpc;
using Google.Protobuf;

namespace Centra.Providers.Flotilla.Tests.Unit;

/// <summary>
/// Mock HTTP handler for in-memory gRPC testing without network sockets.
/// </summary>
public sealed class MockFlotillaGrpcHttpHandler : HttpMessageHandler
{
    public ProposalResponse ResponseToReturn { get; set; } = new()
    {
        Success = true,
        Index = 100,
        Term = 1,
        LeaderId = 1,
    };

    public byte[]? CapturedPayload { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length >= 5)
            {
                var payloadLength = (bytes[1] << 24) | (bytes[2] << 16) | (bytes[3] << 8) | bytes[4];
                var protoBytes = bytes.AsSpan(5, payloadLength);
                var req = ProposalRequest.Parser.ParseFrom(protoBytes.ToArray());
                CapturedPayload = req.Payload.ToByteArray();
            }
        }

        var resBytes = ResponseToReturn.ToByteArray();
        var responsePayload = new byte[5 + resBytes.Length];
        responsePayload[0] = 0; // Uncompressed
        responsePayload[1] = (byte)((resBytes.Length >> 24) & 0xFF);
        responsePayload[2] = (byte)((resBytes.Length >> 16) & 0xFF);
        responsePayload[3] = (byte)((resBytes.Length >> 8) & 0xFF);
        responsePayload[4] = (byte)(resBytes.Length & 0xFF);
        resBytes.CopyTo(responsePayload, 5);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(responsePayload)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc");
        response.TrailingHeaders.Add("grpc-status", "0");

        return response;
    }
}
