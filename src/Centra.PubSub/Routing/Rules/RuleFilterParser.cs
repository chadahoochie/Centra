using System.Globalization;

namespace Centra.PubSub.Routing.Rules;

internal sealed class RuleFilterParser
{
    internal readonly IReadOnlyList<RuleFilterToken> Tokens;
    internal int Position;

    public RuleFilterParser(IReadOnlyList<RuleFilterToken> tokens)
    {
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Position = 0;
    }

    public static IRuleExpression ParseExpression(string expression)
    {
        var tokens = RuleFilterTokenizer.Tokenize(expression);
        var parser = new RuleFilterParser(tokens);
        return parser.Parse();
    }

    public IRuleExpression Parse()
    {
        if (Tokens.Count == 0)
        {
            return new ConstantRuleExpression(true);
        }

        var expr = ParseOr();
        if (Position < Tokens.Count)
        {
            var tok = Tokens[Position];
            throw new FormatException($"Unexpected token '{tok.Value}' at position {tok.Position}");
        }

        return expr;
    }

    internal IRuleExpression ParseOr()
    {
        var left = ParseAnd();

        while (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.Or)
        {
            var op = Tokens[Position].Type;
            Position++;
            var right = ParseAnd();
            left = new BinaryRuleExpression(left, op, right);
        }

        return left;
    }

    internal IRuleExpression ParseAnd()
    {
        var left = ParseComparison();

        while (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.And)
        {
            var op = Tokens[Position].Type;
            Position++;
            var right = ParseComparison();
            left = new BinaryRuleExpression(left, op, right);
        }

        return left;
    }

    internal IRuleExpression ParseComparison()
    {
        var left = ParseUnary();

        if (Position < Tokens.Count)
        {
            var curType = Tokens[Position].Type;
            if (curType is RuleFilterTokenType.Equals or
                RuleFilterTokenType.NotEquals or
                RuleFilterTokenType.LessThan or
                RuleFilterTokenType.LessThanOrEqual or
                RuleFilterTokenType.GreaterThan or
                RuleFilterTokenType.GreaterThanOrEqual or
                RuleFilterTokenType.In)
            {
                Position++;
                var right = ParseUnary();
                return new ComparisonRuleExpression(left, curType, right);
            }
        }

        return left;
    }

    internal IRuleExpression ParseUnary()
    {
        if (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.Not)
        {
            var op = Tokens[Position].Type;
            Position++;
            var operand = ParseUnary();
            return new UnaryRuleExpression(op, operand);
        }

        return ParsePrimary();
    }

    internal IRuleExpression ParsePrimary()
    {
        if (Position >= Tokens.Count)
        {
            throw new FormatException("Unexpected end of expression");
        }

        var token = Tokens[Position];

        if (token.Type == RuleFilterTokenType.OpenParen)
        {
            Position++;
            var expr = ParseOr();
            if (Position >= Tokens.Count || Tokens[Position].Type != RuleFilterTokenType.CloseParen)
            {
                throw new FormatException($"Missing closing parenthesis for '(' at position {token.Position}");
            }
            Position++;
            return expr;
        }

        if (token.Type == RuleFilterTokenType.OpenBracket)
        {
            Position++;
            var items = new List<object?>();
            while (Position < Tokens.Count && Tokens[Position].Type != RuleFilterTokenType.CloseBracket)
            {
                var itemExpr = ParseUnary();
                var evaluated = itemExpr.Evaluate(default, null, new Dictionary<string, string>());
                items.Add(evaluated);

                if (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.Comma)
                {
                    Position++;
                }
            }

            if (Position >= Tokens.Count || Tokens[Position].Type != RuleFilterTokenType.CloseBracket)
            {
                throw new FormatException($"Missing closing bracket ']' at position {token.Position}");
            }
            Position++;
            return new ConstantRuleExpression(items);
        }

        if (token.Type == RuleFilterTokenType.StringLiteral)
        {
            Position++;
            return new ConstantRuleExpression(token.Value);
        }

        if (token.Type == RuleFilterTokenType.NumberLiteral)
        {
            Position++;
            if (long.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            {
                return new ConstantRuleExpression(longVal);
            }
            if (double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dblVal))
            {
                return new ConstantRuleExpression(dblVal);
            }
            throw new FormatException($"Invalid number literal '{token.Value}' at position {token.Position}");
        }

        if (token.Type == RuleFilterTokenType.BooleanLiteral)
        {
            Position++;
            return new ConstantRuleExpression(bool.Parse(token.Value));
        }

        if (token.Type == RuleFilterTokenType.NullLiteral)
        {
            Position++;
            return new ConstantRuleExpression(null);
        }

        if (token.Type == RuleFilterTokenType.Identifier)
        {
            Position++;

            // Check if top-level function call, e.g. contains(event.type, 'order')
            if (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.OpenParen)
            {
                var funcName = token.Value;
                Position++;
                var args = new List<IRuleExpression>();
                while (Position < Tokens.Count && Tokens[Position].Type != RuleFilterTokenType.CloseParen)
                {
                    args.Add(ParseOr());
                    if (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.Comma)
                    {
                        Position++;
                    }
                }

                if (Position >= Tokens.Count || Tokens[Position].Type != RuleFilterTokenType.CloseParen)
                {
                    throw new FormatException($"Missing closing parenthesis ')' for function '{funcName}'");
                }
                Position++;
                return new FunctionCallRuleExpression(funcName, args);
            }

            // Path navigation: e.g. event.type or headers['ce-type'] or data.items[0]
            var path = new List<string> { token.Value };

            while (Position < Tokens.Count)
            {
                if (Tokens[Position].Type == RuleFilterTokenType.Dot)
                {
                    Position++;
                    if (Position >= Tokens.Count || Tokens[Position].Type != RuleFilterTokenType.Identifier)
                    {
                        throw new FormatException($"Expected identifier after '.' at position {Position}");
                    }

                    var propToken = Tokens[Position];
                    Position++;

                    // Check for method call on property: e.g. event.type.startsWith('order.')
                    if (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.OpenParen)
                    {
                        var methodName = propToken.Value;
                        Position++;
                        var args = new List<IRuleExpression> { new PropertyAccessRuleExpression(path) };
                        while (Position < Tokens.Count && Tokens[Position].Type != RuleFilterTokenType.CloseParen)
                        {
                            args.Add(ParseOr());
                            if (Position < Tokens.Count && Tokens[Position].Type == RuleFilterTokenType.Comma)
                            {
                                Position++;
                            }
                        }

                        if (Position >= Tokens.Count || Tokens[Position].Type != RuleFilterTokenType.CloseParen)
                        {
                            throw new FormatException($"Missing closing parenthesis ')' for method '{methodName}'");
                        }
                        Position++;
                        return new FunctionCallRuleExpression(methodName, args);
                    }

                    path.Add(propToken.Value);
                }
                else if (Tokens[Position].Type == RuleFilterTokenType.OpenBracket)
                {
                    Position++;
                    if (Position >= Tokens.Count || (Tokens[Position].Type != RuleFilterTokenType.StringLiteral && Tokens[Position].Type != RuleFilterTokenType.Identifier))
                    {
                        throw new FormatException($"Expected key inside brackets at position {Position}");
                    }
                    path.Add(Tokens[Position].Value);
                    Position++;

                    if (Position >= Tokens.Count || Tokens[Position].Type != RuleFilterTokenType.CloseBracket)
                    {
                        throw new FormatException($"Missing closing bracket ']' at position {Position}");
                    }
                    Position++;
                }
                else
                {
                    break;
                }
            }

            return new PropertyAccessRuleExpression(path);
        }

        throw new FormatException($"Unexpected token '{token.Value}' of type {token.Type} at position {token.Position}");
    }
}
