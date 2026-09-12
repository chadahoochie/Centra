namespace Centra.Bindings;

internal static class CronMacroParser
{
    internal static readonly Dictionary<string, string> StandardMacros = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@hourly"] = "0 * * * *",
        ["@daily"] = "0 0 * * *",
        ["@midnight"] = "0 0 * * *",
        ["@weekly"] = "0 0 * * 0",
        ["@monthly"] = "0 0 1 * *",
        ["@yearly"] = "0 0 1 1 *",
        ["@annually"] = "0 0 1 1 *",
    };

    internal static CronExpressionParser ParseMacro(string macro)
    {
        if (macro.StartsWith("@every ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseEveryMacro(macro.AsSpan(7).Trim(), macro);
        }

        if (StandardMacros.TryGetValue(macro, out var cron))
        {
            return CronExpressionParser.Parse(cron);
        }

        throw new FormatException($"Unknown cron macro: '{macro}'.");
    }

    internal static CronExpressionParser ParseEveryMacro(ReadOnlySpan<char> span, string macro)
    {
        if (span.IsEmpty)
        {
            throw new FormatException($"Invalid @every macro: '{macro}' has no duration.");
        }

        var unit = span[^1];
        var numSpan = span[..^1];
        if (!int.TryParse(numSpan, out var num) || num <= 0)
        {
            throw new FormatException($"Invalid @every duration value: '{span.ToString()}'.");
        }

        var duration = ResolveEveryDuration(unit, num);
        return new CronExpressionParser(duration);
    }

    internal static TimeSpan ResolveEveryDuration(char unit, int num)
    {
        return char.ToLowerInvariant(unit) switch
        {
            's' => TimeSpan.FromSeconds(num),
            'm' => TimeSpan.FromMinutes(num),
            'h' => TimeSpan.FromHours(num),
            'd' => TimeSpan.FromDays(num),
            _ => throw new FormatException($"Unsupported @every time unit '{unit}'. Use s, m, h, or d.")
        };
    }
}
