using Centra.Events;

namespace Centra.Hosting.Routing;

internal static class CentraEventContextExtractor
{
    public static EventContext Extract(
        string pubSubName,
        string topic,
        IReadOnlyDictionary<string, string> headers)
    {
        var id = headers.TryGetValue(CloudEventConstants.IdHeader, out var hId) ? hId : string.Empty;
        var source = headers.TryGetValue(CloudEventConstants.SourceHeader, out var hSource) ? hSource : "unknown";
        var type = headers.TryGetValue(CloudEventConstants.TypeHeader, out var hType) ? hType : string.Empty;
        var time = headers.TryGetValue(CloudEventConstants.TimeHeader, out var hTime) && DateTimeOffset.TryParse(hTime, out var parsedTime)
            ? parsedTime
            : DateTimeOffset.UtcNow;

        var correlationId = headers.TryGetValue(CloudEventConstants.CorrelationIdHeader, out var hCorr) ? hCorr : null;
        var causationId = headers.TryGetValue(CloudEventConstants.CausationIdHeader, out var hCause) ? hCause : null;
        var tenantId = headers.TryGetValue(CloudEventConstants.TenantIdHeader, out var hTenant) ? hTenant : null;

        return new EventContext(
            Id: id,
            Topic: topic,
            PubSubName: pubSubName,
            Source: source,
            Type: type,
            Timestamp: time,
            CorrelationId: correlationId,
            CausationId: causationId,
            TenantId: tenantId,
            Headers: headers);
    }
}
