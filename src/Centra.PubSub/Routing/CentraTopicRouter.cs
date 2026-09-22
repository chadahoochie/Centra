using System.Text.Json;
using Centra.Events;
using Centra.PubSub;

namespace Centra.PubSub.Routing;

public sealed class CentraTopicRouter
{
    private readonly CentraTopicRegistration[] _filteredRoutes;
    private readonly CentraTopicRegistration? _defaultRoute;

    public string PubSubName { get; }

    public string Topic { get; }

    public IReadOnlyList<CentraTopicRegistration> AllRoutes { get; }

    public CentraTopicRegistration? DefaultRoute => _defaultRoute;

    public bool RequiresDataPayload { get; }

    public PubSubSubscribeOptions SubscriptionOptions { get; }

    public string? DeadLetterTopic { get; }

    public CentraTopicRouter(string pubSubName, string topic, IEnumerable<CentraTopicRegistration> registrations)
    {
        PubSubName = pubSubName ?? throw new ArgumentNullException(nameof(pubSubName));
        Topic = topic ?? throw new ArgumentNullException(nameof(topic));

        var all = registrations.ToArray();
        AllRoutes = all;

        var filtered = new List<CentraTopicRegistration>();
        CentraTopicRegistration? defaultRoute = null;
        var requiresData = false;

        // Sort by Priority descending
        var sorted = all.OrderByDescending(r => r.Priority).ToArray();

        foreach (var reg in sorted)
        {
            if (reg.CompiledFilter is not null || reg.Predicate is not null || !string.IsNullOrWhiteSpace(reg.RuleFilter))
            {
                filtered.Add(reg);
                if (reg.CompiledFilter?.RequiresDataPayload == true || reg.Predicate is not null)
                {
                    requiresData = true;
                }
            }
            else if (defaultRoute is null)
            {
                defaultRoute = reg;
            }
        }

        _filteredRoutes = filtered.ToArray();
        _defaultRoute = defaultRoute;
        RequiresDataPayload = requiresData;

        var (options, dlq) = CentraTopicSubscriptionOptionsResolver.Resolve(all);
        SubscriptionOptions = options;
        DeadLetterTopic = dlq;
    }

    public CentraTopicRegistration? SelectRoute(
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers,
        in EventContext context)
    {
        JsonDocument? jsonDoc = null;
        JsonElement? rootElement = null;

        if (RequiresDataPayload && !payload.IsEmpty)
        {
            try
            {
                jsonDoc = JsonDocument.Parse(payload);
                rootElement = jsonDoc.RootElement;
            }
            catch (JsonException)
            {
                rootElement = null;
            }
        }

        try
        {
            for (var i = 0; i < _filteredRoutes.Length; i++)
            {
                var route = _filteredRoutes[i];

                if (route.CompiledFilter is not null)
                {
                    if (route.CompiledFilter.Evaluate(in context, rootElement, headers))
                    {
                        return route;
                    }
                }
                else if (route.Predicate is not null)
                {
                    if (route.Predicate(context, payload))
                    {
                        return route;
                    }
                }
            }

            return _defaultRoute;
        }
        finally
        {
            jsonDoc?.Dispose();
        }
    }
}
