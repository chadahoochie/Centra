using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub.Routing.Rules;

internal sealed class BinaryRuleExpression : IRuleExpression
{
    public IRuleExpression Left { get; }

    public RuleFilterTokenType Operator { get; }

    public IRuleExpression Right { get; }

    public bool RequiresDataPayload => Left.RequiresDataPayload || Right.RequiresDataPayload;

    public BinaryRuleExpression(IRuleExpression left, RuleFilterTokenType op, IRuleExpression right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Operator = op;
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public object? Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers)
    {
        var leftVal = Left.Evaluate(in context, data, headers);
        var leftBool = IsTruthy(leftVal);

        if (Operator == RuleFilterTokenType.And)
        {
            if (!leftBool)
            {
                return false;
            }

            var rightVal = Right.Evaluate(in context, data, headers);
            return IsTruthy(rightVal);
        }

        if (Operator == RuleFilterTokenType.Or)
        {
            if (leftBool)
            {
                return true;
            }

            var rightVal = Right.Evaluate(in context, data, headers);
            return IsTruthy(rightVal);
        }

        throw new InvalidOperationException($"Unsupported binary operator: {Operator}");
    }

    internal static bool IsTruthy(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        if (RuleValueComparator.TryConvertToDouble(value, out var d))
        {
            return Math.Abs(d) > 0.0000001;
        }

        if (value is string s)
        {
            if (bool.TryParse(s, out var parsedBool))
            {
                return parsedBool;
            }
            return !string.IsNullOrWhiteSpace(s);
        }

        return true;
    }
}
