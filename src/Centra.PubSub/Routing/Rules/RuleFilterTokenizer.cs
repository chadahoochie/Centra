namespace Centra.PubSub.Routing.Rules;

internal static class RuleFilterTokenizer
{
    public static IReadOnlyList<RuleFilterToken> Tokenize(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var tokens = new List<RuleFilterToken>();
        var span = expression.AsSpan();
        var i = 0;

        while (i < span.Length)
        {
            var ch = span[i];

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            var startPos = i;

            if (ch == '\'' || ch == '"')
            {
                var quote = ch;
                i++;
                var strStart = i;
                while (i < span.Length && span[i] != quote)
                {
                    if (span[i] == '\\' && i + 1 < span.Length)
                    {
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                if (i >= span.Length)
                {
                    throw new FormatException($"Unterminated string literal at position {startPos} in rule filter: '{expression}'");
                }

                var strVal = span[strStart..i].ToString().Replace("\\'", "'").Replace("\\\"", "\"");
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.StringLiteral, strVal, startPos));
                i++;
                continue;
            }

            if (char.IsDigit(ch))
            {
                var numStart = i;
                while (i < span.Length && (char.IsDigit(span[i]) || span[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new RuleFilterToken(RuleFilterTokenType.NumberLiteral, span[numStart..i].ToString(), startPos));
                continue;
            }

            if (ch == '=' && i + 1 < span.Length && span[i + 1] == '=')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.Equals, "==", startPos));
                i += 2;
                continue;
            }

            if (ch == '!' && i + 1 < span.Length && span[i + 1] == '=')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.NotEquals, "!=", startPos));
                i += 2;
                continue;
            }

            if (ch == '<' && i + 1 < span.Length && span[i + 1] == '=')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.LessThanOrEqual, "<=", startPos));
                i += 2;
                continue;
            }

            if (ch == '<')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.LessThan, "<", startPos));
                i++;
                continue;
            }

            if (ch == '>' && i + 1 < span.Length && span[i + 1] == '=')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.GreaterThanOrEqual, ">=", startPos));
                i += 2;
                continue;
            }

            if (ch == '>')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.GreaterThan, ">", startPos));
                i++;
                continue;
            }

            if (ch == '&' && i + 1 < span.Length && span[i + 1] == '&')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.And, "&&", startPos));
                i += 2;
                continue;
            }

            if (ch == '|' && i + 1 < span.Length && span[i + 1] == '|')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.Or, "||", startPos));
                i += 2;
                continue;
            }

            if (ch == '!')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.Not, "!", startPos));
                i++;
                continue;
            }

            if (ch == '(')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.OpenParen, "(", startPos));
                i++;
                continue;
            }

            if (ch == ')')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.CloseParen, ")", startPos));
                i++;
                continue;
            }

            if (ch == '[')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.OpenBracket, "[", startPos));
                i++;
                continue;
            }

            if (ch == ']')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.CloseBracket, "]", startPos));
                i++;
                continue;
            }

            if (ch == ',')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.Comma, ",", startPos));
                i++;
                continue;
            }

            if (ch == '.')
            {
                tokens.Add(new RuleFilterToken(RuleFilterTokenType.Dot, ".", startPos));
                i++;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_' || ch == '$')
            {
                var idStart = i;
                while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] == '_' || span[i] == '-' || span[i] == '$'))
                {
                    i++;
                }

                var text = span[idStart..i].ToString();

                if (string.Equals(text, "and", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.And, text, startPos));
                }
                else if (string.Equals(text, "or", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.Or, text, startPos));
                }
                else if (string.Equals(text, "not", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.Not, text, startPos));
                }
                else if (string.Equals(text, "in", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.In, text, startPos));
                }
                else if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.BooleanLiteral, "true", startPos));
                }
                else if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.BooleanLiteral, "false", startPos));
                }
                else if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.NullLiteral, "null", startPos));
                }
                else
                {
                    tokens.Add(new RuleFilterToken(RuleFilterTokenType.Identifier, text, startPos));
                }

                continue;
            }

            throw new FormatException($"Unexpected character '{ch}' at position {startPos} in rule filter: '{expression}'");
        }

        return tokens;
    }
}
