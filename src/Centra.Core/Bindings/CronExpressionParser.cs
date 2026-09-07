namespace Centra.Bindings;

public sealed class CronExpressionParser
{
    private readonly ulong _secondMask;
    private readonly ulong _minuteMask;
    private readonly ulong _hourMask;
    private readonly ulong _dayOfMonthMask;
    private readonly ulong _monthMask;
    private readonly byte _dayOfWeekMask;
    private readonly TimeSpan? _interval;

    private CronExpressionParser(
        ulong secondMask,
        ulong minuteMask,
        ulong hourMask,
        ulong dayOfMonthMask,
        ulong monthMask,
        byte dayOfWeekMask)
    {
        _secondMask = secondMask;
        _minuteMask = minuteMask;
        _hourMask = hourMask;
        _dayOfMonthMask = dayOfMonthMask;
        _monthMask = monthMask;
        _dayOfWeekMask = dayOfWeekMask;
        _interval = null;
    }

    private CronExpressionParser(TimeSpan interval)
    {
        _interval = interval;
        _secondMask = 0;
        _minuteMask = 0;
        _hourMask = 0;
        _dayOfMonthMask = 0;
        _monthMask = 0;
        _dayOfWeekMask = 0;
    }

    public static CronExpressionParser Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var trimmed = expression.Trim();

        if (trimmed.StartsWith('@'))
        {
            return ParseMacro(trimmed);
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5 && parts.Length != 6)
        {
            throw new FormatException($"Cron expression '{expression}' must contain 5 or 6 fields, but found {parts.Length}.");
        }

        int index = 0;
        ulong secondMask;
        if (parts.Length == 6)
        {
            secondMask = ParseField(parts[index++], 0, 59, "seconds");
        }
        else
        {
            secondMask = 1UL << 0; // Default to second 0
        }

        var minuteMask = ParseField(parts[index++], 0, 59, "minutes");
        var hourMask = ParseField(parts[index++], 0, 23, "hours");
        var dayOfMonthMask = ParseField(parts[index++], 1, 31, "day-of-month");
        var monthMask = ParseField(parts[index++], 1, 12, "month");
        var dayOfWeekMask = (byte)ParseField(parts[index], 0, 7, "day-of-week");

        return new CronExpressionParser(secondMask, minuteMask, hourMask, dayOfMonthMask, monthMask, dayOfWeekMask);
    }

    private static readonly Dictionary<string, string> StandardMacros = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@hourly"] = "0 * * * *",
        ["@daily"] = "0 0 * * *",
        ["@midnight"] = "0 0 * * *",
        ["@weekly"] = "0 0 * * 0",
        ["@monthly"] = "0 0 1 * *",
        ["@yearly"] = "0 0 1 1 *",
        ["@annually"] = "0 0 1 1 *",
    };

    private static CronExpressionParser ParseMacro(string macro)
    {
        if (macro.StartsWith("@every ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseEveryMacro(macro.AsSpan(7).Trim(), macro);
        }

        if (StandardMacros.TryGetValue(macro, out var cron))
        {
            return Parse(cron);
        }

        throw new FormatException($"Unknown cron macro: '{macro}'.");
    }

    private static CronExpressionParser ParseEveryMacro(ReadOnlySpan<char> span, string macro)
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

    private static TimeSpan ResolveEveryDuration(char unit, int num)
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

    private static ulong ParseField(string field, int min, int max, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new FormatException($"Field '{fieldName}' cannot be empty.");
        }

        ulong mask = 0;
        var subParts = field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (subParts.Length == 0)
        {
            throw new FormatException($"Field '{fieldName}' has no valid elements.");
        }

        foreach (var part in subParts)
        {
            mask |= ParseSubPart(part, min, max, fieldName);
        }

        return mask;
    }

    private static ulong ParseSubPart(string part, int min, int max, string fieldName)
    {
        var (rangePart, step) = ExtractStep(part, fieldName);
        var (rangeStart, rangeEnd) = ParseRange(rangePart, step, part.IndexOf('/') >= 0, min, max, fieldName);

        ulong mask = 0;
        for (int i = rangeStart; i <= rangeEnd; i += step)
        {
            mask |= 1UL << i;
        }

        return mask;
    }

    private static (string RangePart, int Step) ExtractStep(string part, string fieldName)
    {
        var stepIndex = part.IndexOf('/');
        if (stepIndex < 0)
        {
            return (part, 1);
        }

        var stepStr = part[(stepIndex + 1)..];
        if (!int.TryParse(stepStr, out var step) || step <= 0)
        {
            throw new FormatException($"Invalid step '{stepStr}' in field '{fieldName}'. Step must be greater than 0.");
        }

        return (part[..stepIndex], step);
    }

    private static (int Start, int End) ParseRange(string rangePart, int step, bool hasStep, int min, int max, string fieldName)
    {
        if (rangePart == "*")
        {
            return (min, max);
        }

        var dashIndex = rangePart.IndexOf('-');
        if (dashIndex >= 0)
        {
            return ParseExplicitRange(rangePart, dashIndex, min, max, fieldName);
        }

        return ParseSingleValue(rangePart, hasStep, min, max, fieldName);
    }

    private static (int Start, int End) ParseExplicitRange(string rangePart, int dashIndex, int min, int max, string fieldName)
    {
        var startStr = rangePart[..dashIndex];
        var endStr = rangePart[(dashIndex + 1)..];

        if (!int.TryParse(startStr, out var rangeStart) || rangeStart < min || rangeStart > max)
        {
            throw new FormatException($"Invalid range start '{startStr}' in field '{fieldName}'. Range: {min}-{max}.");
        }

        if (!int.TryParse(endStr, out var rangeEnd) || rangeEnd < min || rangeEnd > max || rangeEnd < rangeStart)
        {
            throw new FormatException($"Invalid range end '{endStr}' in field '{fieldName}'. Range: {min}-{max}.");
        }

        return (rangeStart, rangeEnd);
    }

    private static (int Start, int End) ParseSingleValue(string rangePart, bool hasStep, int min, int max, string fieldName)
    {
        if (!int.TryParse(rangePart, out var singleValue) || singleValue < min || singleValue > max)
        {
            throw new FormatException($"Invalid value '{rangePart}' in field '{fieldName}'. Value must be between {min} and {max}.");
        }

        return hasStep ? (singleValue, max) : (singleValue, singleValue);
    }

    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from)
    {
        if (_interval.HasValue)
        {
            return from.Add(_interval.Value);
        }

        var current = new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, from.Minute, from.Second, from.Offset).AddSeconds(1);
        int maxYear = from.Year + 5;

        while (current.Year <= maxYear)
        {
            if ((_monthMask & (1UL << current.Month)) == 0)
            {
                current = new DateTimeOffset(current.Year, current.Month, 1, 0, 0, 0, current.Offset).AddMonths(1);
                continue;
            }

            var daysInMonth = DateTime.DaysInMonth(current.Year, current.Month);
            if (current.Day > daysInMonth || (_dayOfMonthMask & (1UL << current.Day)) == 0)
            {
                current = new DateTimeOffset(current.Year, current.Month, current.Day, 0, 0, 0, current.Offset).AddDays(1);
                continue;
            }

            var dow = (int)current.DayOfWeek;
            if ((_dayOfWeekMask & (1 << dow)) == 0 && (dow != 0 || (_dayOfWeekMask & (1 << 7)) == 0))
            {
                current = new DateTimeOffset(current.Year, current.Month, current.Day, 0, 0, 0, current.Offset).AddDays(1);
                continue;
            }

            if ((_hourMask & (1UL << current.Hour)) == 0)
            {
                current = new DateTimeOffset(current.Year, current.Month, current.Day, current.Hour, 0, 0, current.Offset).AddHours(1);
                continue;
            }

            if ((_minuteMask & (1UL << current.Minute)) == 0)
            {
                current = new DateTimeOffset(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0, current.Offset).AddMinutes(1);
                continue;
            }

            if ((_secondMask & (1UL << current.Second)) == 0)
            {
                current = current.AddSeconds(1);
                continue;
            }

            return current;
        }

        return null;
    }
}
