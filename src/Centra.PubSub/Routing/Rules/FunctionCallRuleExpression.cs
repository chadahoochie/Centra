using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal sealed class FunctionCallRuleExpression : IRuleExpression
{
    public string FunctionName { get; }

    public IReadOnlyList<IRuleExpression> Arguments { get; }

    public bool RequiresDataPayload
    {
        get
        {
            for (var i = 0; i < Arguments.Count; i++)
            {
                if (Arguments[i].RequiresDataPayload)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public FunctionCallRuleExpression(string functionName, IReadOnlyList<IRuleExpression> arguments)
    {
        FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    public object? Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers)
    {
        if (Arguments.Count < 2)
        {
            return false;
        }

        var target = Arguments[0].Evaluate(in context, data, headers)?.ToString() ?? string.Empty;
        var pattern = Arguments[1].Evaluate(in context, data, headers)?.ToString() ?? string.Empty;

        if (string.Equals(FunctionName, "contains", StringComparison.OrdinalIgnoreCase))
        {
            return target.Contains(pattern, StringComparison.Ordinal);
        }

        if (string.Equals(FunctionName, "startswith", StringComparison.OrdinalIgnoreCase))
        {
            return target.StartsWith(pattern, StringComparison.Ordinal);
        }

        if (string.Equals(FunctionName, "endswith", StringComparison.OrdinalIgnoreCase))
        {
            return target.EndsWith(pattern, StringComparison.Ordinal);
        }

        throw new NotSupportedException($"Unsupported function: '{FunctionName}'");
    }
}
