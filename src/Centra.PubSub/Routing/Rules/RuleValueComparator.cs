using System.Collections;
using System.Globalization;

namespace Centra.PubSub.Routing.Rules;

internal static class RuleValueComparator
{
    public static bool AreEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (TryConvertToDouble(left, out var leftNum) && TryConvertToDouble(right, out var rightNum))
        {
            return Math.Abs(leftNum - rightNum) < 0.0000001;
        }

        if (left is bool leftBool && (right is bool rightBool || bool.TryParse(right.ToString(), out rightBool)))
        {
            return leftBool == rightBool;
        }

        return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    public static int Compare(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (TryConvertToDouble(left, out var leftNum) && TryConvertToDouble(right, out var rightNum))
        {
            return leftNum.CompareTo(rightNum);
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    public static bool IsIn(object? item, object? collection)
    {
        if (item is null || collection is null)
        {
            return false;
        }

        if (collection is IEnumerable enumerable && collection is not string)
        {
            foreach (var elem in enumerable)
            {
                if (AreEqual(item, elem))
                {
                    return true;
                }
            }
            return false;
        }

        if (collection is string strColl)
        {
            return strColl.Contains(item.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        return false;
    }

    internal static bool TryConvertToDouble(object value, out double result)
    {
        if (value is double d)
        {
            result = d;
            return true;
        }

        if (value is float f)
        {
            result = f;
            return true;
        }

        if (value is int i)
        {
            result = i;
            return true;
        }

        if (value is long l)
        {
            result = l;
            return true;
        }

        if (value is decimal dec)
        {
            result = (double)dec;
            return true;
        }

        if (value is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = 0;
        return false;
    }
}
