using System;
using System.Globalization;

namespace Ttfx.Utils;

/// <summary>
/// The original constructor argument, preserved because upstream <c>Color.__eq__</c>
/// and <c>__hash__</c> compare <c>color_arg</c> — <c>Color(255) != Color("ffffff")</c>
/// even when they resolve to the same RGB. Dict/set keying depends on this.
/// Transcribed from <c>utils/graphics.rs</c>.
/// </summary>
public abstract class ColorArg : IEquatable<ColorArg>
{
    private ColorArg()
    {
    }

    public sealed class Xterm : ColorArg
    {
        public byte Code { get; }

        public Xterm(byte code)
        {
            Code = code;
        }

        public override bool Equals(ColorArg? other) => other is Xterm x && Code == x.Code;

        public override int GetHashCode() => Code.GetHashCode();
    }

    public sealed class Hex : ColorArg
    {
        public string Value { get; }

        public Hex(string value)
        {
            Value = value;
        }

        public override bool Equals(ColorArg? other) =>
            other is Hex hex && Value == hex.Value;

        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    }

    public abstract bool Equals(ColorArg? other);

    public override bool Equals(object? obj) => obj is ColorArg other && Equals(other);

    public override int GetHashCode() => base.GetHashCode();
}

/// <summary>
/// Color. Equality is on the original argument (plan §5.10). hex_to_xterm is issue 0005.
/// </summary>
public sealed class Color : IEquatable<Color>
{
    public string Original { get; }

    public ColorArg ColorArg { get; }

    /// <summary>Some(code) when constructed from an xterm int, None for hex strings.</summary>
    public byte? XtermColor { get; }

    /// <summary>hex string without '#'.</summary>
    public string RgbColor { get; }

    private readonly byte[] _rgb;

    private Color(string original, ColorArg colorArg, byte? xtermColor, string rgbColor, byte[] rgb)
    {
        Original = original;
        ColorArg = colorArg;
        XtermColor = xtermColor;
        RgbColor = rgbColor;
        _rgb = rgb;
    }

    public static Color FromXterm(byte code) =>
        FromXterm(code, code.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Xterm constructor. <paramref name="original"/> is the CLI token when
    /// built from <c>parse_color</c>; equality is on that token (plan §5.10).
    /// </summary>
    public static Color FromXterm(byte code, string original)
    {
        string rgbColor = Hexterm.XtermToHexColor(code);
        return new Color(
            original: original,
            colorArg: new ColorArg.Xterm(code),
            xtermColor: code,
            rgbColor: rgbColor,
            rgb: ParseRgb(rgbColor));
    }

    /// <summary>Hex-string constructor. Errors mirror upstream ValueError.</summary>
    public static Color FromHex(string hex)
    {
        string stripped = TrimMatches(hex, '#');
        if (!Hexterm.IsValidHexColor(stripped))
        {
            throw new ArgumentException(
                "Invalid color value. Color must be an XTerm-256 color code or an RGB hex color string. "
                + "Example: 255 or 'ffffff' or '#ffffff'");
        }

        string rgbColor = stripped;
        return new Color(
            original: hex,
            colorArg: new ColorArg.Hex(rgbColor),
            xtermColor: null,
            rgbColor: rgbColor,
            rgb: ParseRgb(rgbColor));
    }

    public (byte R, byte G, byte B) RgbInts() => (_rgb[0], _rgb[1], _rgb[2]);

    /// <summary>
    /// Equality is on the original constructor token (plan §5.10):
    /// <c>Color(255) != Color("ffffff")</c> even when RGB matches.
    /// </summary>
    public bool Equals(Color? other)
    {
        if (other is null)
        {
            return false;
        }

        return Original == other.Original;
    }

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => Original.GetHashCode(StringComparison.Ordinal);

    private static byte[] ParseRgb(string s)
    {
        return
        [
            byte.Parse(s.AsSpan(0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            byte.Parse(s.AsSpan(2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            byte.Parse(s.AsSpan(4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
        ];
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
}

public sealed class ColorPair
{
    public Color? FgColor { get; }
    public Color? BgColor { get; }

    public ColorPair(Color? fg = null, Color? bg = null)
    {
        FgColor = fg;
        BgColor = bg;
    }

    public static ColorPair New(Color? fg, Color? bg) => new ColorPair(fg, bg);
}
