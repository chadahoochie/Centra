using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal static class PropertyValueResolver
{
    public static object? ResolveProperty(
        IReadOnlyList<string> pathSegments,
        in EventContext context,
        JsonElement? data,
        IReadOnlyDictionary<string, string> headers)
    {
        if (pathSegments.Count == 0)
        {
            return null;
        }

        var first = pathSegments[0];

        if (string.Equals(first, "headers", StringComparison.OrdinalIgnoreCase))
        {
            if (pathSegments.Count < 2)
            {
                return null;
            }

            var headerKey = pathSegments[1];
            return headers.TryGetValue(headerKey, out var headerVal) ? headerVal : null;
        }

        if (string.Equals(first, "event", StringComparison.OrdinalIgnoreCase))
        {
            if (pathSegments.Count < 2)
            {
                return null;
            }

            var second = pathSegments[1];
            if (string.Equals(second, "data", StringComparison.OrdinalIgnoreCase))
            {
                if (!data.HasValue)
                {
                    return null;
                }

                var current = data.Value;
                for (var idx = 2; idx < pathSegments.Count; idx++)
                {
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(pathSegments[idx], out var next))
                    {
                        return null;
                    }
                    current = next;
                }

                return ExtractJsonValue(current);
            }

            return ResolveEnvelopeProperty(second, in context, headers);
        }

        if (string.Equals(first, "data", StringComparison.OrdinalIgnoreCase))
        {
            if (!data.HasValue)
            {
                return null;
            }

            var current = data.Value;
            for (var idx = 1; idx < pathSegments.Count; idx++)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(pathSegments[idx], out var next))
                {
                    return null;
                }
                current = next;
            }

            return ExtractJsonValue(current);
        }

        // Shorthand for envelope properties (type, source, id, etc.)
        if (pathSegments.Count == 1)
        {
            var envVal = ResolveEnvelopeProperty(first, in context, headers);
            if (envVal is not null)
            {
                return envVal;
            }
        }

        // Fallback to searching data if present
        if (data.HasValue && data.Value.ValueKind == JsonValueKind.Object)
        {
            var current = data.Value;
            for (var idx = 0; idx < pathSegments.Count; idx++)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(pathSegments[idx], out var next))
                {
                    return null;
                }
                current = next;
            }

            return ExtractJsonValue(current);
        }

        return null;
    }

    internal static object? ResolveEnvelopeProperty(
        string propertyName,
        in EventContext context,
        IReadOnlyDictionary<string, string> headers)
    {
        if (string.Equals(propertyName, "type", StringComparison.OrdinalIgnoreCase))
        {
            return context.Type;
        }

        if (string.Equals(propertyName, "source", StringComparison.OrdinalIgnoreCase))
        {
            return context.Source;
        }

        if (string.Equals(propertyName, "id", StringComparison.OrdinalIgnoreCase))
        {
            return context.Id;
        }

        if (string.Equals(propertyName, "topic", StringComparison.OrdinalIgnoreCase))
        {
            return context.Topic;
        }

        if (string.Equals(propertyName, "pubsubname", StringComparison.OrdinalIgnoreCase))
        {
            return context.PubSubName;
        }

        if (string.Equals(propertyName, "subject", StringComparison.OrdinalIgnoreCase))
        {
            return headers.TryGetValue(CloudEventConstants.SubjectHeader, out var sub) ? sub : null;
        }

        if (string.Equals(propertyName, "datacontenttype", StringComparison.OrdinalIgnoreCase))
        {
            return headers.TryGetValue(CloudEventConstants.DataContentTypeHeader, out var dct) ? dct : null;
        }

        if (string.Equals(propertyName, "correlationid", StringComparison.OrdinalIgnoreCase))
        {
            return context.CorrelationId;
        }

        if (string.Equals(propertyName, "causationid", StringComparison.OrdinalIgnoreCase))
        {
            return context.CausationId;
        }

        if (string.Equals(propertyName, "tenantid", StringComparison.OrdinalIgnoreCase))
        {
            return context.TenantId;
        }

        if (string.Equals(propertyName, "time", StringComparison.OrdinalIgnoreCase))
        {
            return context.Timestamp.ToString("O");
        }

        return null;
    }

    internal static object? ExtractJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }
}
