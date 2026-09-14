using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal sealed class PropertyAccessRuleExpression : IRuleExpression
{
    public IReadOnlyList<string> PathSegments { get; }

    public bool RequiresDataPayload { get; }

    public PropertyAccessRuleExpression(IReadOnlyList<string> pathSegments)
    {
        PathSegments = pathSegments ?? throw new ArgumentNullException(nameof(pathSegments));

        if (pathSegments.Count > 0)
        {
            var first = pathSegments[0];
            if (string.Equals(first, "data", StringComparison.OrdinalIgnoreCase))
            {
                RequiresDataPayload = true;
            }
            else if (string.Equals(first, "event", StringComparison.OrdinalIgnoreCase) &&
                     pathSegments.Count > 1 &&
                     string.Equals(pathSegments[1], "data", StringComparison.OrdinalIgnoreCase))
            {
                RequiresDataPayload = true;
            }
            else if (!string.Equals(first, "event", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(first, "headers", StringComparison.OrdinalIgnoreCase))
            {
                // Ambiguous or shorthand property that could be in payload data
                RequiresDataPayload = true;
            }
            else
            {
                RequiresDataPayload = false;
            }
        }
        else
        {
            RequiresDataPayload = false;
        }
    }

    public object? Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers)
    {
        return PropertyValueResolver.ResolveProperty(PathSegments, in context, data, headers);
    }
}
