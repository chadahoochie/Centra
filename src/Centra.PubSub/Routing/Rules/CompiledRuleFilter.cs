using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal sealed class CompiledRuleFilter : ICompiledRuleFilter
{
    private readonly IRuleExpression _expression;

    public string Expression { get; }

    public bool RequiresDataPayload => _expression.RequiresDataPayload;

    public CompiledRuleFilter(string expression, IRuleExpression ruleExpression)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        _expression = ruleExpression ?? throw new ArgumentNullException(nameof(ruleExpression));
    }

    public bool Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers)
    {
        var result = _expression.Evaluate(in context, data, headers);
        return BinaryRuleExpression.IsTruthy(result);
    }
}
