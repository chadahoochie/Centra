namespace Centra.Bindings;

internal static class CronFieldParser
{
    internal static ulong ParseField(string field, int min, int max, string fieldName)
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

    internal static ulong ParseSubPart(string part, int min, int max, string fieldName)
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

    internal static (string RangePart, int Step) ExtractStep(string part, string fieldName)
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

    internal static (int Start, int End) ParseRange(string rangePart, int step, bool hasStep, int min, int max, string fieldName)
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

    internal static (int Start, int End) ParseExplicitRange(string rangePart, int dashIndex, int min, int max, string fieldName)
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

    internal static (int Start, int End) ParseSingleValue(string rangePart, bool hasStep, int min, int max, string fieldName)
    {
        if (!int.TryParse(rangePart, out var singleValue) || singleValue < min || singleValue > max)
        {
            throw new FormatException($"Invalid value '{rangePart}' in field '{fieldName}'. Value must be between {min} and {max}.");
        }

        return hasStep ? (singleValue, max) : (singleValue, singleValue);
    }
}
