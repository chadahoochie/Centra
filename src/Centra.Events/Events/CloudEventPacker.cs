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
        IReadOnlyDictionary<string, string>? additionalMetadata = null)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var attr = typeof(T).GetCustomAttribute<EventContractAttribute>();
        var (eventType, schemaVersion, dataSchema) = attr is not null
            ? (attr.Type, attr.Version, attr.Schema)
            : (typeof(T).Name, null, null);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

        if (mode == CloudEventMode.Binary)
        {
            headers[CloudEventConstants.DataContentTypeHeader] = CloudEventConstants.DefaultContentType;
            var payload = Serializer.Serialize(data);
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
                DataContentType = CloudEventConstants.DefaultContentType,
                DataSchema = dataSchema,
                Subject = subject,
                Time = now,
                Data = data,
                CorrelationId = CentraAmbientContext.CorrelationId,
                CausationId = CentraAmbientContext.CausationId,
                TenantId = CentraAmbientContext.TenantId,
                SchemaVersion = schemaVersion
            };

            var payload = Serializer.Serialize(cloudEvent);
            return new PackedCloudEvent(payload, headers, CloudEventMode.Structured);
        }
    }
}
