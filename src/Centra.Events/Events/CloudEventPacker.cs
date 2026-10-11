using System.Diagnostics;
using System.Reflection;
using Centra.Diagnostics;
using Centra.Memory;
using Centra.Serialization;

namespace Centra.Events;

public static class CloudEventPacker
{
    private static readonly ICentraSerializer Serializer = JsonCentraSerializer.Default;

    public static PackedCloudEvent Pack<T>(
        T data,
        string source,
        CloudEventMode mode = CloudEventMode.Binary,
        string? subject = null,
        IReadOnlyDictionary<string, string>? additionalMetadata = null,
        ICentraSerializer? serializer = null,
        string? contentType = null)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var eventType = EventContractMetadataCache<T>.EventType;
        var schemaVersion = EventContractMetadataCache<T>.SchemaVersion;
        var dataSchema = EventContractMetadataCache<T>.DataSchema;

        var headerCapacity = 8 + (additionalMetadata?.Count ?? 0)
            + (subject is not null ? 1 : 0)
            + (schemaVersion is not null ? 1 : 0)
            + (dataSchema is not null ? 1 : 0);
        var headers = new Dictionary<string, string>(headerCapacity, StringComparer.OrdinalIgnoreCase)
        {
            [CloudEventConstants.IdHeader] = eventId,
            [CloudEventConstants.SourceHeader] = source,
            [CloudEventConstants.SpecVersionHeader] = CloudEventConstants.SpecVersion10,
            [CloudEventConstants.TypeHeader] = eventType,
            [CloudEventConstants.TimeHeader] = now.ToString("O")
        };

        if (subject is not null)
        {
            headers[CloudEventConstants.SubjectHeader] = subject;
        }

        if (schemaVersion is not null)
        {
            headers[CloudEventConstants.SchemaVersionHeader] = schemaVersion;
        }

        if (dataSchema is not null)
        {
            headers[CloudEventConstants.DataSchemaHeader] = dataSchema;
        }

        // Ambient Enterprise Context
        if (CentraAmbientContext.CorrelationId is not null)
        {
            headers[CloudEventConstants.CorrelationIdHeader] = CentraAmbientContext.CorrelationId;
        }

        if (CentraAmbientContext.CausationId is not null)
        {
            headers[CloudEventConstants.CausationIdHeader] = CentraAmbientContext.CausationId;
        }

        if (CentraAmbientContext.TenantId is not null)
        {
            headers[CloudEventConstants.TenantIdHeader] = CentraAmbientContext.TenantId;
        }

        // Inject Distributed Tracing
        CentraTracePropagator.Inject(Activity.Current, headers);

        // Additional user metadata
        if (additionalMetadata is not null)
        {
            foreach (var kvp in additionalMetadata)
            {
                headers[kvp.Key] = kvp.Value;
            }
        }

        var effectiveSerializer = serializer ?? Serializer;
        var effectiveContentType = contentType
            ?? (headers.TryGetValue(CloudEventConstants.DataContentTypeHeader, out var customCt) ? customCt : effectiveSerializer.ContentType);

        if (mode == CloudEventMode.Binary)
        {
            headers[CloudEventConstants.DataContentTypeHeader] = effectiveContentType;
            var payload = effectiveSerializer.Serialize(data);
            return new PackedCloudEvent(payload, headers, CloudEventMode.Binary);
        }
        else
        {
            headers[CloudEventConstants.DataContentTypeHeader] = "application/cloudevents+json";
            var cloudEvent = new CloudEvent<T>
            {
                Id = eventId,
                Source = source,
                SpecVersion = CloudEventConstants.SpecVersion10,
                Type = eventType,
                DataContentType = effectiveContentType,
                DataSchema = dataSchema,
                Subject = subject,
                Time = now,
                Data = data,
                CorrelationId = CentraAmbientContext.CorrelationId,
                CausationId = CentraAmbientContext.CausationId,
                TenantId = CentraAmbientContext.TenantId,
                SchemaVersion = schemaVersion
            };

            var payload = effectiveSerializer.Serialize(cloudEvent);
            return new PackedCloudEvent(payload, headers, CloudEventMode.Structured);
        }
    }
}
