using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal sealed class ConstantRuleExpression : IRuleExpression
{
    public object? Value { get; }

    public bool RequiresDataPayload => false;

    public ConstantRuleExpression(object? value)
    {
        Value = value;
    }

    public object? Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers)
    {
        return Value;
    }
}
