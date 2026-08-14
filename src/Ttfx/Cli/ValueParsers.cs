using System;
using System.Globalization;
using System.Text;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Cli;

/// <summary>
/// Rust <c>str::parse</c> + argutils messages. Not <c>long.Parse</c> /
/// <c>double.Parse</c> (those accept surrounding whitespace; Rust rejects).
/// </summary>
public static class ValueParsers
{
    private const ulong RustNanBits = 0x7FF8000000000000UL;
    private const ulong RustNegNanBits = 0xFFF8000000000000UL;

    public static bool TryParseI64(string s, out long value)
    {
        value = 0;
        if (!IsRustSignedIntGrammar(s))
        {
            return false;
        }

        return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseU64(string s, out ulong value)
    {
        value = 0;
        if (!IsRustUnsignedIntGrammar(s))
        {
            return false;
        }

        return ulong.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseF64(string s, out double value)
    {
        value = 0;
        if (s.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                return false;
            }
        }

        int start = 0;
        bool negative = false;
        if (s[0] == '+')
        {
            start = 1;
        }
        else if (s[0] == '-')
        {
            negative = true;
            start = 1;
        }

        if (start >= s.Length)
        {
            return false;
        }

        ReadOnlySpan<char> body = s.AsSpan(start);
        if (body.Equals("inf", StringComparison.OrdinalIgnoreCase)
            || body.Equals("infinity", StringComparison.OrdinalIgnoreCase))
        {
            value = negative ? double.NegativeInfinity : double.PositiveInfinity;
            return true;
        }

        if (body.Equals("nan", StringComparison.OrdinalIgnoreCase))
        {
            value = BitConverter.UInt64BitsToDouble(negative ? RustNegNanBits : RustNanBits);
            return true;
        }

        if (!IsRustFloatGrammar(s))
        {
            return false;
        }

        return double.TryParse(
            s,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static long ParseI64(string s)
    {
        if (!TryParseI64(s, out long value))
        {
            throw new UsageError($"invalid int value: '{s}'");
        }

        return value;
    }

    public static ulong ParseU64(string s)
    {
        if (!TryParseU64(s, out ulong value))
        {
            throw new UsageError($"invalid int value: '{s}'");
        }

        return value;
    }

    public static double ParseF64(string s)
    {
        if (!TryParseF64(s, out double value))
        {
            throw new UsageError($"invalid float value: '{s}'");
        }

        return value;
    }

    public static object ParsePositiveInt(string s) => PositiveInt(s);

    public static long PositiveInt(string s)
    {
        long v = ParseI64(s);
        if (v > 0)
        {
            return v;
        }

        throw new UsageError($"{v} is not > 0");
    }

    public static object ParseNonNegativeInt(string s) => NonNegativeInt(s);

    public static long NonNegativeInt(string s)
    {
        long v = ParseI64(s);
        if (v >= 0)
        {
            return v;
        }

        throw new UsageError($"{v} is not >= 0");
    }

    public static object ParseCanvasDimension(string s) => CanvasDimension(s);

    public static long CanvasDimension(string s)
    {
        long v = ParseI64(s);
        if (v >= -1)
        {
            return v;
        }

        throw new UsageError($"{v} is not >= -1");
    }

    public static object ParseU64Object(string s) => ParseU64(s);

    public static object ParseSeed(string s)
    {
        if (!TryParseU64(s, out ulong value))
        {
            throw new UsageError($"invalid int value: '{s}'");
        }

        return value;
    }

    public static double PositiveFloat(string s)
    {
        double v = ParseF64(s);
        if (v > 0.0)
        {
            return v;
        }

        throw new UsageError($"{FormatF64(v)} is not a valid value. Argument must be a float > 0.");
    }

    public static double NonNegativeFloat(string s)
    {
        double v = ParseF64(s);
        if (v >= 0.0)
        {
            return v;
        }

        throw new UsageError($"{FormatF64(v)} is not a valid value. Argument must be a float >= 0.");
    }

    public static double NonNegativeRatio(string s)
    {
        double v = ParseF64(s);
        if (v >= 0.0 && v <= 1.0)
        {
            return v;
        }

        throw new UsageError($"{FormatF64(v)} is not a valid value. Argument must be a float 0 <= n <= 1.");
    }

    public static double PositiveRatio(string s)
    {
        double v = ParseF64(s);
        if (v > 0.0 && v <= 1.0)
        {
            return v;
        }

        throw new UsageError($"{FormatF64(v)} is not a valid value. Argument must be a float 0 < n <= 1.");
    }

    public static object ParseColorArg(string s) => ColorArg(s);

    public static Color ColorArg(string s)
    {
        // Rust s.len() is bytes. Color tokens are ASCII-safe by construction.
        int byteLen = Encoding.UTF8.GetByteCount(s);
        if (byteLen <= 3)
        {
            if (!TryParseU8(s, out _))
            {
                throw new UsageError($"invalid color value: '{s}'");
            }

            return new Color(s);
        }

        if (!IsValidHexColorArg(s))
        {
            throw new UsageError(
                "Invalid color value. Color must be an XTerm-256 color code or an RGB hex color string. "
                + "Example: 255 or 'ffffff' or '#ffffff'");
        }

        return new Color(s);
    }

    public static object ParseAnchor(string s) => Anchor(s);

    public static Anchor Anchor(string s)
    {
        Anchor? parsed = AnchorParse.Parse(s);
        if (parsed is null)
        {
            throw new UsageError($"invalid anchor: '{s}'");
        }

        return parsed.Value;
    }

    public static object ParseEasingName(string s) => EasingName(s);

    public static Easing EasingName(string s)
    {
        Easing? parsed = EasingParse.Parse(s);
        if (parsed is null)
        {
            throw new UsageError($"invalid easing function: '{s}'");
        }

        return parsed.Value;
    }

    public static object ParseSymbol(string s) => Symbol(s);

    public static string Symbol(string s)
    {
        var enumerator = s.EnumerateRunes();
        if (!enumerator.MoveNext())
        {
            throw new UsageError($"invalid symbol: '{s}' argument must be a single character");
        }

        if (enumerator.MoveNext())
        {
            throw new UsageError($"invalid symbol: '{s}' argument must be a single character");
        }

        return s;
    }

    public static object ParseExistingColorHandling(string s) => ExistingColorHandling(s);

    public static ExistingColorHandling ExistingColorHandling(string s)
    {
        return s switch
        {
            "always" => Engine.ExistingColorHandling.Always,
            "dynamic" => Engine.ExistingColorHandling.Dynamic,
            "ignore" => Engine.ExistingColorHandling.Ignore,
            _ => throw new UsageError($"invalid choice: '{s}' (choose from 'always', 'dynamic', 'ignore')"),
        };
    }

    public static object ParseCompletionShell(string s)
    {
        if (s == "bash" || s == "zsh")
        {
            return s;
        }

        throw new UsageError($"invalid choice: '{s}' (choose from 'bash', 'zsh')");
    }

    public static object ParseString(string s) => s;

    public static object ParseInputFile(string s) => s;

    private static bool TryParseU8(string s, out byte value)
    {
        value = 0;
        if (!TryParseU64(s, out ulong v) || v > 255)
        {
            return false;
        }

        value = (byte)v;
        return true;
    }

    private static bool IsRustSignedIntGrammar(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        int i = 0;
        if (s[0] == '+' || s[0] == '-')
        {
            i = 1;
        }

        if (i >= s.Length)
        {
            return false;
        }

        for (; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRustUnsignedIntGrammar(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        int i = 0;
        if (s[0] == '+')
        {
            i = 1;
        }

        if (i >= s.Length)
        {
            return false;
        }

        for (; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRustFloatGrammar(string s)
    {
        int i = 0;
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
        {
            i = 1;
        }

        if (i >= s.Length)
        {
            return false;
        }

        bool sawDigit = false;
        if (s[i] == '.')
        {
            i++;
            if (i >= s.Length || s[i] < '0' || s[i] > '9')
            {
                return false;
            }

            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }

            sawDigit = true;
        }
        else
        {
            if (s[i] < '0' || s[i] > '9')
            {
                return false;
            }

            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }

            sawDigit = true;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                {
                    i++;
                }
            }
        }

        if (!sawDigit)
        {
            return false;
        }

        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                i++;
            }

            if (i >= s.Length || s[i] < '0' || s[i] > '9')
            {
                return false;
            }

            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }
        }

        return i == s.Length;
    }

    private static bool IsValidHexColorArg(string hex)
    {
        string stripped = TrimMatches(hex, '#');
        string startTrimmed = TrimStartMatches(stripped, '#');
        int strippedLen = Encoding.UTF8.GetByteCount(startTrimmed);
        if (strippedLen != 6 && strippedLen != 7)
        {
            return false;
        }

        return TryParseHexI64(TrimMatches(stripped, '#'));
    }

    private static string TrimMatches(string s, char c)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && s[start] == c)
        {
            start++;
        }

        while (end > start && s[end - 1] == c)
        {
            end--;
        }

        return s.Substring(start, end - start);
    }

    private static string TrimStartMatches(string s, char c)
    {
        int start = 0;
        while (start < s.Length && s[start] == c)
        {
            start++;
        }

        return s.Substring(start);
    }

    private static bool TryParseHexI64(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        int i = 0;
        if (s[0] == '+' || s[0] == '-')
        {
            i = 1;
        }

        if (i >= s.Length)
        {
            return false;
        }

        for (; i < s.Length; i++)
        {
            if (HexDigit(s[i]) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'a' && c <= 'f')
        {
            return c - 'a' + 10;
        }

        if (c >= 'A' && c <= 'F')
        {
            return c - 'A' + 10;
        }

        return -1;
    }

    private static string FormatF64(double v)
    {
        if (double.IsPositiveInfinity(v))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(v))
        {
            return "-inf";
        }

        if (double.IsNaN(v))
        {
            return "NaN";
        }

        return v.ToString(CultureInfo.InvariantCulture);
    }
}
