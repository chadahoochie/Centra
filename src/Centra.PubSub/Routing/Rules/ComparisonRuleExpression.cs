using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal sealed class ComparisonRuleExpression : IRuleExpression
{
    public IRuleExpression Left { get; }

    public RuleFilterTokenType Operator { get; }

    public IRuleExpression Right { get; }

    public bool RequiresDataPayload => Left.RequiresDataPayload || Right.RequiresDataPayload;

    public ComparisonRuleExpression(IRuleExpression left, RuleFilterTokenType op, IRuleExpression right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Operator = op;
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public object? Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers)
    {
        var leftVal = Left.Evaluate(in context, data, headers);
        var rightVal = Right.Evaluate(in context, data, headers);

        return Operator switch
        {
            RuleFilterTokenType.Equals => RuleValueComparator.AreEqual(leftVal, rightVal),
            RuleFilterTokenType.NotEquals => !RuleValueComparator.AreEqual(leftVal, rightVal),
            RuleFilterTokenType.LessThan => leftVal is not null && rightVal is not null && RuleValueComparator.Compare(leftVal, rightVal) < 0,
            RuleFilterTokenType.LessThanOrEqual => leftVal is not null && rightVal is not null && RuleValueComparator.Compare(leftVal, rightVal) <= 0,
            RuleFilterTokenType.GreaterThan => leftVal is not null && rightVal is not null && RuleValueComparator.Compare(leftVal, rightVal) > 0,
            RuleFilterTokenType.GreaterThanOrEqual => leftVal is not null && rightVal is not null && RuleValueComparator.Compare(leftVal, rightVal) >= 0,
            RuleFilterTokenType.In => RuleValueComparator.IsIn(leftVal, rightVal),
            _ => throw new InvalidOperationException($"Unsupported comparison operator: {Operator}")
        };
    }
}
