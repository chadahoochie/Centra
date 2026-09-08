using System.Text.Json;
using Centra.Diagnostics;
using Centra.Serialization;

namespace Centra.Events;

public static class CloudEventUnpacker
{
    private static readonly ICentraSerializer Serializer = JsonCentraSerializer.Default;

    public static UnpackedCloudEvent<T> Unpack<T>(
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers)
    {
        // Check if structured mode by content type
        var isStructured = headers.TryGetValue(CloudEventConstants.DataContentTypeHeader, out var ct)
            && ct.Contains("cloudevents", StringComparison.OrdinalIgnoreCase);

        if (isStructured)
        {
            var envelope = Serializer.Deserialize<CloudEvent<T>>(payload);
            if (envelope is null)
            {
                throw new InvalidOperationException("Failed to deserialize structured CloudEvent envelope");
            }

            var context = new EventContext(
                Id: envelope.Id,
                Topic: headers.TryGetValue("centra-topic", out var topic) ? topic : string.Empty,
                PubSubName: headers.TryGetValue("centra-pubsub", out var pubsub) ? pubsub : string.Empty,
                Source: envelope.Source,
                Type: envelope.Type,
                Timestamp: envelope.Time ?? DateTimeOffset.UtcNow,
                CorrelationId: envelope.CorrelationId,
                CausationId: envelope.CausationId,
                TenantId: envelope.TenantId,
                Headers: headers);

            return new UnpackedCloudEvent<T>(envelope.Data, context);
        }
        else
        {
            // Binary mode: domain data is the raw payload
            var data = Serializer.Deserialize<T>(payload);

            var id = headers.TryGetValue(CloudEventConstants.IdHeader, out var hId) ? hId : Guid.NewGuid().ToString("N");
            var source = headers.TryGetValue(CloudEventConstants.SourceHeader, out var hSource) ? hSource : "unknown";
            var type = headers.TryGetValue(CloudEventConstants.TypeHeader, out var hType) ? hType : typeof(T).Name;
            var time = headers.TryGetValue(CloudEventConstants.TimeHeader, out var hTime) && DateTimeOffset.TryParse(hTime, out var parsedTime)
                ? parsedTime
                : DateTimeOffset.UtcNow;

            var correlationId = headers.TryGetValue(CloudEventConstants.CorrelationIdHeader, out var hCorr) ? hCorr : null;
            var causationId = headers.TryGetValue(CloudEventConstants.CausationIdHeader, out var hCause) ? hCause : null;
            var tenantId = headers.TryGetValue(CloudEventConstants.TenantIdHeader, out var hTenant) ? hTenant : null;
            var topic = headers.TryGetValue("centra-topic", out var hTopic) ? hTopic : string.Empty;
            var pubsub = headers.TryGetValue("centra-pubsub", out var hPubsub) ? hPubsub : string.Empty;

            var context = new EventContext(
                Id: id,
                Topic: topic,
                PubSubName: pubsub,
                Source: source,
                Type: type,
                Timestamp: time,
                CorrelationId: correlationId,
                CausationId: causationId,
                TenantId: tenantId,
                Headers: headers);

            return new UnpackedCloudEvent<T>(data, context);
        }
    }
}
