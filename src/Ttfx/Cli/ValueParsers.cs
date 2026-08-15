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

    /// <summary>common.rs parse_positive_float: float &gt; 0, boxed for OptionSpec.</summary>
    public static object ParsePositiveFloat(string s) => PositiveFloat(s);

    /// <summary>common.rs parse_positive_int_range: "1-10".</summary>
    public static object ParsePositiveIntRange(string s)
    {
        int dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            throw new UsageError($"invalid range: '{s}'");
        }

        string aStr = s.Substring(0, dash);
        string bStr = s.Substring(dash + 1);
        if (!TryParseI64(aStr, out long a) || !TryParseI64(bStr, out long b))
        {
            throw new UsageError($"invalid range: '{s}'");
        }

        if (a > 0 && a <= b)
        {
            return (a, b);
        }

        throw new UsageError($"invalid range: '{s}'");
    }

    /// <summary>common.rs parse_positive_float_range: "0.25-0.5".</summary>
    public static object ParsePositiveFloatRange(string s)
    {
        int dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            throw new UsageError($"invalid range: '{s}'");
        }

        string aStr = s.Substring(0, dash);
        string bStr = s.Substring(dash + 1);
        if (!TryParseF64(aStr, out double a) || !TryParseF64(bStr, out double b))
        {
            throw new UsageError($"invalid range: '{s}'");
        }

        if (a > 0.0 && a <= b)
        {
            return (a, b);
        }

        throw new UsageError($"invalid range: '{s}'");
    }

    /// <summary>common.rs parse_non_negative_ratio: 0 &lt;= n &lt;= 1, boxed for OptionSpec.</summary>
    public static object ParseNonNegativeRatio(string s) => NonNegativeRatio(s);

    /// <summary>common.rs parse_positive_ratio: 0 &lt; n &lt;= 1, boxed for OptionSpec.</summary>
    public static object ParsePositiveRatio(string s) => PositiveRatio(s);

    public static double NonNegativeFloat(string s)
    {
        double v = ParseF64(s);
        if (v >= 0.0)
        {
            return v;
        }

        throw new UsageError($"{FormatF64(v)} is not a valid value. Argument must be a float >= 0.");
    }

    /// <summary>common.rs parse_non_negative_float: float &gt;= 0, boxed for OptionSpec.</summary>
    public static object ParseNonNegativeFloat(string s) => NonNegativeFloat(s);

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
            if (!TryParseU8(s, out byte code))
            {
                throw new UsageError($"invalid color value: '{s}'");
            }

            return Color.FromXterm(code, s);
        }

        if (!IsValidHexColorArg(s))
        {
            throw new UsageError(
                "Invalid color value. Color must be an XTerm-256 color code or an RGB hex color string. "
                + "Example: 255 or 'ffffff' or '#ffffff'");
        }

        return Color.FromHex(s);
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
        // common.rs:116 — s.chars().count() == 1 (runes, not UTF-16 length)
        if (Unicode.RuneCount(s) != 1)
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

    public static object ParseI64Object(string s) => ParseI64(s);

    /// <summary>common.rs parse_positive_int: alias of parse_gradient_steps.</summary>
    public static object ParseCommonPositiveInt(string s) => ParseGradientSteps(s);

    /// <summary>common.rs parse_gradient_steps: int &gt; 0.</summary>
    public static object ParseGradientSteps(string s)
    {
        long v = ParseI64(s);
        if (v > 0)
        {
            return v;
        }

        throw new UsageError($"{v} is not a valid value. Argument must be an int > 0.");
    }

    /// <summary>common.rs parse_non_negative_int: int &gt;= 0.</summary>
    public static object ParseCommonNonNegativeInt(string s)
    {
        long v = ParseI64(s);
        if (v >= 0)
        {
            return v;
        }

        throw new UsageError($"{v} is not a valid value. Argument must be an int >= 0.");
    }

    /// <summary>common.rs parse_gradient_direction.</summary>
    public static object ParseGradientDirection(string s)
    {
        return s switch
        {
            "horizontal" => GradientDirection.Horizontal,
            "vertical" => GradientDirection.Vertical,
            "diagonal" => GradientDirection.Diagonal,
            "radial" => GradientDirection.Radial,
            _ => throw new UsageError($"invalid gradient direction: '{s}'"),
        };
    }

    /// <summary>waves.rs parse_wave_direction — subset of CharacterGroup.</summary>
    public static object ParseWaveDirection(string s)
    {
        return s switch
        {
            "column_left_to_right" => CharacterGroup.ColumnLeftToRight,
            "column_right_to_left" => CharacterGroup.ColumnRightToLeft,
            "row_top_to_bottom" => CharacterGroup.RowTopToBottom,
            "row_bottom_to_top" => CharacterGroup.RowBottomToTop,
            "center_to_outside" => CharacterGroup.CenterToOutside,
            "outside_to_center" => CharacterGroup.OutsideToCenter,
            _ => throw new UsageError($"invalid wave direction: '{s}'"),
        };
    }

    /// <summary>common.rs parse_character_group.</summary>
    public static object ParseCharacterGroup(string s)
    {
        return s switch
        {
            "column_left_to_right" => CharacterGroup.ColumnLeftToRight,
            "column_right_to_left" => CharacterGroup.ColumnRightToLeft,
            "row_top_to_bottom" => CharacterGroup.RowTopToBottom,
            "row_bottom_to_top" => CharacterGroup.RowBottomToTop,
            "diagonal_top_left_to_bottom_right" => CharacterGroup.DiagonalTopLeftToBottomRight,
            "diagonal_bottom_left_to_top_right" => CharacterGroup.DiagonalBottomLeftToTopRight,
            "diagonal_top_right_to_bottom_left" => CharacterGroup.DiagonalTopRightToBottomLeft,
            "diagonal_bottom_right_to_top_left" => CharacterGroup.DiagonalBottomRightToTopLeft,
            "center_to_outside" => CharacterGroup.CenterToOutside,
            "outside_to_center" => CharacterGroup.OutsideToCenter,
            _ => throw new UsageError($"invalid character group: '{s}'"),
        };
    }

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
