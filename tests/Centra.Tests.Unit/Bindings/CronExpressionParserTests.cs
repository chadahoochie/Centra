using Centra.Bindings;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class CronExpressionParserTests
{
    [Fact]
    public void Parse_EveryMinute_ShouldReturnNextMinute()
    {
        var parser = CronExpressionParser.Parse("* * * * *");
        var from = new DateTimeOffset(2026, 9, 7, 10, 15, 30, TimeSpan.Zero);

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next.Value.ShouldBe(new DateTimeOffset(2026, 9, 7, 10, 16, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_EveryFiveMinutes_ShouldReturnNextMultipleOfFive()
    {
        var parser = CronExpressionParser.Parse("*/5 * * * *");
        var from = new DateTimeOffset(2026, 9, 7, 10, 12, 0, TimeSpan.Zero);

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next.Value.ShouldBe(new DateTimeOffset(2026, 9, 7, 10, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_SpecificHourAndMinute_ShouldReturnNextMatch()
    {
        var parser = CronExpressionParser.Parse("30 9 * * 1-5"); // 9:30 AM Mon-Fri
        // Sunday Sep 6, 2026
        var from = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        // Next Monday is Sep 7, 2026 at 9:30 AM
        next.Value.ShouldBe(new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero));
        next.Value.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Fact]
    public void Parse_SixFieldWithSeconds_ShouldTriggerOnSecondMatch()
    {
        var parser = CronExpressionParser.Parse("*/15 * * * * *"); // Every 15 seconds
        var from = new DateTimeOffset(2026, 9, 7, 10, 0, 7, TimeSpan.Zero);

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next.Value.ShouldBe(new DateTimeOffset(2026, 9, 7, 10, 0, 15, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("0 0 * * 0", DayOfWeek.Sunday)]
    [InlineData("0 0 * * 7", DayOfWeek.Sunday)] // 7 is also Sunday
    public void Parse_SundayVariants_ShouldBothMatchSunday(string expression, DayOfWeek expectedDay)
    {
        var parser = CronExpressionParser.Parse(expression);
        var from = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero); // Monday Sep 7

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next.Value.DayOfWeek.ShouldBe(expectedDay);
    }

    [Fact]
    public void Parse_MonthEndRollOver_ShouldHandleDecemberToJanuary()
    {
        var parser = CronExpressionParser.Parse("0 0 1 1 *"); // Midnight Jan 1st
        var from = new DateTimeOffset(2026, 12, 31, 23, 50, 0, TimeSpan.Zero);

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next.Value.ShouldBe(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_LeapYear_ShouldAdvanceToFeb29InLeapYear()
    {
        var parser = CronExpressionParser.Parse("0 0 29 2 *"); // Midnight Feb 29
        var from = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero); // 2027 is not leap, 2028 is

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next.Value.ShouldBe(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("@every 10s", 10)]
    [InlineData("@every 1m", 60)]
    [InlineData("@every 2h", 7200)]
    public void Parse_EveryMacro_ShouldAdvanceByCorrectInterval(string macro, int expectedSeconds)
    {
        var parser = CronExpressionParser.Parse(macro);
        var from = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        var next = parser.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next.Value.ShouldBe(from.AddSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData("@hourly", "0 * * * *")]
    [InlineData("@daily", "0 0 * * *")]
    [InlineData("@weekly", "0 0 * * 0")]
    [InlineData("@monthly", "0 0 1 * *")]
    public void Parse_StandardMacros_ShouldMatchEquivalentCron(string macro, string equivalentCron)
    {
        var macroParser = CronExpressionParser.Parse(macro);
        var cronParser = CronExpressionParser.Parse(equivalentCron);
        var from = new DateTimeOffset(2026, 9, 7, 15, 30, 22, TimeSpan.Zero);

        var macroNext = macroParser.GetNextOccurrence(from);
        var cronNext = cronParser.GetNextOccurrence(from);

        macroNext.ShouldBe(cronNext);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrWhitespace_ShouldThrowArgumentException(string invalid)
    {
        Should.Throw<ArgumentException>(() => CronExpressionParser.Parse(invalid));
    }

    [Theory]
    [InlineData("* * *")] // Too few fields
    [InlineData("* * * * * * *")] // Too many fields
    [InlineData("60 * * * *")] // Minute > 59
    [InlineData("* 24 * * *")] // Hour > 23
    [InlineData("* * 32 * *")] // Day > 31
    [InlineData("* * * 13 *")] // Month > 12
    [InlineData("* * * * 8")] // Day of week > 7
    [InlineData("*/0 * * * *")] // Step = 0
    [InlineData("foo * * * *")] // Non-numeric text
    [InlineData("@unknown")] // Unknown macro
    [InlineData("@every")] // Missing interval
    [InlineData("@every ")] // Missing duration
    [InlineData("@every 0s")] // Zero duration
    [InlineData("@every 5x")] // Unsupported unit
    [InlineData("@every invalid")] // Bad interval format
    [InlineData("10-5 * * * *")] // Range start > end
    [InlineData("a-b * * * *")] // Non-numeric range
    [InlineData("1-z * * * *")] // Non-numeric range end
    public void Parse_InvalidExpressions_ShouldThrowFormatException(string invalid)
    {
        Should.Throw<FormatException>(() => CronExpressionParser.Parse(invalid));
    }
}
