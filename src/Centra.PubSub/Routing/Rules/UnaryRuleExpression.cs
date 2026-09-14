using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal sealed class UnaryRuleExpression : IRuleExpression
{
    public RuleFilterTokenType Operator { get; }

    public IRuleExpression Operand { get; }

    public bool RequiresDataPayload => Operand.RequiresDataPayload;

    public UnaryRuleExpression(RuleFilterTokenType op, IRuleExpression operand)
    {
        Operator = op;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    public object? Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers)
    {
        var val = Operand.Evaluate(in context, data, headers);

        if (Operator == RuleFilterTokenType.Not)
        {
            return !BinaryRuleExpression.IsTruthy(val);
        }

        throw new InvalidOperationException($"Unsupported unary operator: {Operator}");
    }
}
