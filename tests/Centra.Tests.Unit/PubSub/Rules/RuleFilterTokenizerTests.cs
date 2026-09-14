using Centra.PubSub.Routing.Rules;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub.Rules;

public sealed class RuleFilterTokenizerTests
{
    [Fact]
    public void Tokenize_EmptyOrWhitespace_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => RuleFilterTokenizer.Tokenize(""));
        Should.Throw<ArgumentException>(() => RuleFilterTokenizer.Tokenize("   "));
    }

    [Fact]
    public void Tokenize_OperatorsAndKeywords_ScansExpectedTokens()
    {
        var expr = "event.type == 'order.v1' && data.amount > 100 || !(data.active == false)";
        var tokens = RuleFilterTokenizer.Tokenize(expr);

        tokens.Count.ShouldBe(20);
        tokens[0].Type.ShouldBe(RuleFilterTokenType.Identifier);
        tokens[0].Value.ShouldBe("event");
        tokens[1].Type.ShouldBe(RuleFilterTokenType.Dot);
        tokens[2].Type.ShouldBe(RuleFilterTokenType.Identifier);
        tokens[2].Value.ShouldBe("type");
        tokens[3].Type.ShouldBe(RuleFilterTokenType.Equals);
        tokens[4].Type.ShouldBe(RuleFilterTokenType.StringLiteral);
        tokens[4].Value.ShouldBe("order.v1");
        tokens[5].Type.ShouldBe(RuleFilterTokenType.And);
        tokens[6].Type.ShouldBe(RuleFilterTokenType.Identifier);
        tokens[7].Type.ShouldBe(RuleFilterTokenType.Dot);
        tokens[8].Type.ShouldBe(RuleFilterTokenType.Identifier);
        tokens[9].Type.ShouldBe(RuleFilterTokenType.GreaterThan);
        tokens[10].Type.ShouldBe(RuleFilterTokenType.NumberLiteral);
        tokens[10].Value.ShouldBe("100");
        tokens[11].Type.ShouldBe(RuleFilterTokenType.Or);
        tokens[12].Type.ShouldBe(RuleFilterTokenType.Not);
        tokens[13].Type.ShouldBe(RuleFilterTokenType.OpenParen);
    }

    [Fact]
    public void Tokenize_BracketSyntaxAndInOperator_ScansExpectedTokens()
    {
        var expr = "headers['ce-type'] in ['a', 'b']";
        var tokens = RuleFilterTokenizer.Tokenize(expr);

        tokens.Count.ShouldBe(10);
        tokens[0].Type.ShouldBe(RuleFilterTokenType.Identifier);
        tokens[0].Value.ShouldBe("headers");
        tokens[1].Type.ShouldBe(RuleFilterTokenType.OpenBracket);
        tokens[2].Type.ShouldBe(RuleFilterTokenType.StringLiteral);
        tokens[2].Value.ShouldBe("ce-type");
        tokens[3].Type.ShouldBe(RuleFilterTokenType.CloseBracket);
        tokens[4].Type.ShouldBe(RuleFilterTokenType.In);
        tokens[5].Type.ShouldBe(RuleFilterTokenType.OpenBracket);
        tokens[6].Type.ShouldBe(RuleFilterTokenType.StringLiteral);
        tokens[7].Type.ShouldBe(RuleFilterTokenType.Comma);
        tokens[8].Type.ShouldBe(RuleFilterTokenType.StringLiteral);
    }

    [Fact]
    public void Tokenize_ComparisonOperators_ScansCorrectTokens()
    {
        var expr = "<= >= != < >";
        var tokens = RuleFilterTokenizer.Tokenize(expr);

        tokens.Count.ShouldBe(5);
        tokens[0].Type.ShouldBe(RuleFilterTokenType.LessThanOrEqual);
        tokens[1].Type.ShouldBe(RuleFilterTokenType.GreaterThanOrEqual);
        tokens[2].Type.ShouldBe(RuleFilterTokenType.NotEquals);
        tokens[3].Type.ShouldBe(RuleFilterTokenType.LessThan);
        tokens[4].Type.ShouldBe(RuleFilterTokenType.GreaterThan);
    }

    [Fact]
    public void Tokenize_WordKeywords_CaseInsensitive()
    {
        var expr = "AND OR NOT IN TRUE FALSE NULL";
        var tokens = RuleFilterTokenizer.Tokenize(expr);

        tokens.Count.ShouldBe(7);
        tokens[0].Type.ShouldBe(RuleFilterTokenType.And);
        tokens[1].Type.ShouldBe(RuleFilterTokenType.Or);
        tokens[2].Type.ShouldBe(RuleFilterTokenType.Not);
        tokens[3].Type.ShouldBe(RuleFilterTokenType.In);
        tokens[4].Type.ShouldBe(RuleFilterTokenType.BooleanLiteral);
        tokens[5].Type.ShouldBe(RuleFilterTokenType.BooleanLiteral);
        tokens[6].Type.ShouldBe(RuleFilterTokenType.NullLiteral);
    }

    [Fact]
    public void Tokenize_UnterminatedString_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => RuleFilterTokenizer.Tokenize("'unterminated"));
    }

    [Fact]
    public void Tokenize_UnexpectedCharacter_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => RuleFilterTokenizer.Tokenize("event.type == ~foo"));
    }
}
