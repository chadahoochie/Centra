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

    internal CronExpressionParser(
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

    internal CronExpressionParser(TimeSpan interval)
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
            return CronMacroParser.ParseMacro(trimmed);
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
            secondMask = CronFieldParser.ParseField(parts[index++], 0, 59, "seconds");
        }
        else
        {
            secondMask = 1UL << 0; // Default to second 0
        }

        var minuteMask = CronFieldParser.ParseField(parts[index++], 0, 59, "minutes");
        var hourMask = CronFieldParser.ParseField(parts[index++], 0, 23, "hours");
        var dayOfMonthMask = CronFieldParser.ParseField(parts[index++], 1, 31, "day-of-month");
        var monthMask = CronFieldParser.ParseField(parts[index++], 1, 12, "month");
        var dayOfWeekMask = (byte)CronFieldParser.ParseField(parts[index], 0, 7, "day-of-week");

        return new CronExpressionParser(secondMask, minuteMask, hourMask, dayOfMonthMask, monthMask, dayOfWeekMask);
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
