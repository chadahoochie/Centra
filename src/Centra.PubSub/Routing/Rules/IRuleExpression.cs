using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal interface IRuleExpression
{
    bool RequiresDataPayload { get; }

    object? Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers);
}
