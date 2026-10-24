using System.Globalization;

namespace ServiceLib.Aether;

/// <summary>
/// Kotlin's way of reading digits, which PattNG relies on: a digit of any script reads as its ASCII digit, so that
/// "۱۶۲.۱۵۹.۱۹۲.۱" typed on a Persian keyboard is 162.159.192.1. C#'s int.TryParse takes ASCII digits alone.
/// </summary>
public static class AetherDigits
{
    /// <summary>
    /// The value of a decimal digit of any script, as Kotlin's Char.digitToIntOrNull() reads it (a character of the
    /// Unicode category Nd); -1 for anything else.
    /// </summary>
    public static int DigitOf(char ch)
    {
        if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.DecimalDigitNumber)
        {
            return -1;
        }
        return CharUnicodeInfo.GetDecimalDigitValue(ch);
    }

    /// <summary>
    /// The value of a hex digit, as Kotlin's Char.digitToIntOrNull(16) (Java's Character.digit) reads it: a decimal
    /// digit of any script, or a to f in either case, ASCII or full width; -1 for anything else.
    /// </summary>
    public static int HexDigitOf(char ch)
    {
        var digit = DigitOf(ch);
        if (digit >= 0)
        {
            return digit;
        }
        if (ch is >= 'a' and <= 'f')
        {
            return ch - 'a' + 10;
        }
        if (ch is >= 'A' and <= 'F')
        {
            return ch - 'A' + 10;
        }
        if (ch is >= 'ａ' and <= 'ｆ')
        {
            return ch - 'ａ' + 10;
        }
        if (ch is >= 'Ａ' and <= 'Ｆ')
        {
            return ch - 'Ａ' + 10;
        }
        return -1;
    }

    /// <summary>
    /// Kotlin's String.toIntOrNull(): an optional leading '+' or '-', then one or more digits of any script, and a
    /// number an int holds; false for anything else, surrounding spaces among it.
    /// </summary>
    public static bool TryParse(string? text, out int value)
    {
        value = 0;
        if (!TryReadMagnitude(text, int.MaxValue, out var negative, out var magnitude))
        {
            return false;
        }
        value = negative ? (int)-(long)magnitude : (int)magnitude;
        return true;
    }

    /// <summary>
    /// <see cref="TryParse(string?, out int)" /> as Kotlin's toIntOrNull() returns it: null for no number.
    /// </summary>
    public static int? ToIntOrNull(string? text)
    {
        return TryParse(text, out var value) ? value : null;
    }

    /// <summary>
    /// Kotlin's String.toLongOrNull(): <see cref="TryParse(string?, out int)" /> for a number a long holds.
    /// </summary>
    public static bool TryParseLong(string? text, out long value)
    {
        value = 0;
        if (!TryReadMagnitude(text, long.MaxValue, out var negative, out var magnitude))
        {
            return false;
        }
        if (!negative)
        {
            value = (long)magnitude;
        }
        else if (magnitude == (ulong)long.MaxValue + 1)
        {
            value = long.MinValue;
        }
        else
        {
            value = -(long)magnitude;
        }
        return true;
    }

    /// <summary>
    /// <see cref="TryParseLong" /> as Kotlin's toLongOrNull() returns it: null for no number.
    /// </summary>
    public static long? ToLongOrNull(string? text)
    {
        return TryParseLong(text, out var value) ? value : null;
    }

    /// <summary>
    /// The sign and the size of the number <paramref name="text" /> holds, refused when the size goes past
    /// <paramref name="max" /> (one more for a negative number).
    /// </summary>
    private static bool TryReadMagnitude(string? text, long max, out bool negative, out ulong magnitude)
    {
        negative = false;
        magnitude = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        var start = 0;
        // As Kotlin: a first character below '0' can only be a sign, which needs a digit after it.
        if (text[0] < '0')
        {
            if (text.Length == 1)
            {
                return false;
            }
            if (text[0] == '-')
            {
                negative = true;
            }
            else if (text[0] != '+')
            {
                return false;
            }
            start = 1;
        }
        var limit = negative ? (ulong)max + 1 : (ulong)max;
        for (var i = start; i < text.Length; i++)
        {
            var digit = DigitOf(text[i]);
            if (digit < 0)
            {
                return false;
            }
            if (magnitude > (limit - (ulong)digit) / 10)
            {
                return false;
            }
            magnitude = (magnitude * 10) + (ulong)digit;
        }
        return true;
    }
}
