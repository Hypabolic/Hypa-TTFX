using System;
using System.Collections.Generic;
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
/// Color. Equality is on <c>color_arg</c> (plan §5.10 / graphics.rs PartialEq):
/// <c>Color(255) != Color("ffffff")</c> even when RGB matches.
/// <c>#000000</c> vs <c>000000</c> compare equal because Hex stores the stripped rgb_color.
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
    /// built from <c>parse_color</c>; equality is on <c>ColorArg</c> (Xterm code).
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
    /// Equality is on <c>color_arg</c> (graphics.rs:97-106):
    /// <c>Color(255) != Color("ffffff")</c> even when RGB matches.
    /// </summary>
    public bool Equals(Color? other)
    {
        if (other is null)
        {
            return false;
        }

        return ColorArg.Equals(other.ColorArg);
    }

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => ColorArg.GetHashCode();

    private static byte[] ParseRgb(string s) => Hexterm.ParseRgb(s);

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

public sealed class ColorPair : IEquatable<ColorPair>
{
    public Color? FgColor { get; }
    public Color? BgColor { get; }

    public ColorPair(Color? fg = null, Color? bg = null)
    {
        FgColor = fg;
        BgColor = bg;
    }

    public static ColorPair New(Color? fg, Color? bg) => new ColorPair(fg, bg);

    public bool Equals(ColorPair? other)
    {
        if (other is null)
        {
            return false;
        }

        return Equals(FgColor, other.FgColor) && Equals(BgColor, other.BgColor);
    }

    public override bool Equals(object? obj) => obj is ColorPair other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FgColor, BgColor);
}

public enum GradientDirection
{
    Vertical,
    Horizontal,
    Radial,
    Diagonal,
}

/// <summary>
/// Insertion-ordered Coord -&gt; Color mapping (upstream returns a dict; iteration
/// order is Python dict insertion order, which some effects walk).
/// </summary>
public sealed class CoordColorMap
{
    public List<Coord> Order { get; } = new List<Coord>();

    private readonly Dictionary<Coord, Color> _map = new Dictionary<Coord, Color>();

    public void Insert(Coord coord, Color color)
    {
        if (_map.TryAdd(coord, color))
        {
            Order.Add(coord);
        }
        else
        {
            _map[coord] = color;
        }
    }

    public Color? Get(Coord coord) => _map.TryGetValue(coord, out Color? color) ? color : null;

    public IEnumerable<(Coord Coord, Color Color)> Iter()
    {
        foreach (Coord c in Order)
        {
            yield return (c, _map[c]);
        }
    }
}

/// <summary>
/// graphics.Gradient. The spectrum is NOT float lerp: channel deltas use
/// Python integer floor division and the exact end stop is appended per pair
/// (plan.md §5.2).
/// </summary>
public sealed class Gradient
{
    public List<Color> Spectrum { get; }

    public Gradient(List<Color> spectrum)
    {
        Spectrum = spectrum;
    }

    /// <summary>
    /// Gradient(*stops, steps=...). <paramref name="stepsWasInt"/> mirrors the upstream quirk
    /// that only scalar (int) steps are validated before generation.
    /// </summary>
    public static Gradient New(IReadOnlyList<Color> stops, IReadOnlyList<long> steps, bool stepsWasInt, bool doLoop)
    {
        if (stops.Count == 0)
        {
            throw new ArgumentException("At least one stop must be provided.");
        }

        if (stepsWasInt)
        {
            foreach (long step in steps)
            {
                if (step < 1)
                {
                    throw new ArgumentException("Steps must be greater than 0.");
                }
            }
        }

        var spectrum = new List<Color>();
        if (stops.Count == 1)
        {
            for (long n = 0; n < steps[0]; n++)
            {
                spectrum.Add(stops[0]);
            }

            return new Gradient(spectrum);
        }

        var resolvedStops = new List<Color>(stops);
        if (doLoop)
        {
            resolvedStops.Add(resolvedStops[0]);
        }

        int pairCount = resolvedStops.Count - 1;
        var resolvedSteps = new List<long>();
        int take = steps.Count < pairCount ? steps.Count : pairCount;
        for (int i = 0; i < take; i++)
        {
            resolvedSteps.Add(steps[i]);
        }

        while (resolvedSteps.Count < pairCount)
        {
            resolvedSteps.Add(resolvedSteps[resolvedSteps.Count - 1]);
        }

        for (int pairIndex = 0; pairIndex < resolvedSteps.Count; pairIndex++)
        {
            long stepCount = resolvedSteps[pairIndex];
            if (stepCount < 1)
            {
                throw new ArgumentException($"Invalid steps: {stepCount} | Steps must be greater than 0.");
            }

            Color start = resolvedStops[pairIndex];
            Color end = resolvedStops[pairIndex + 1];
            (byte srB, byte sgB, byte sbB) = start.RgbInts();
            (byte erB, byte egB, byte ebB) = end.RgbInts();
            long sr = srB;
            long sg = sgB;
            long sb = sbB;
            long redDelta = PyCompat.FloorDiv(erB - sr, stepCount);
            long greenDelta = PyCompat.FloorDiv(egB - sg, stepCount);
            long blueDelta = PyCompat.FloorDiv(ebB - sb, stepCount);
            long rangeStart = spectrum.Count == 0 ? 0 : 1;
            long rangeEnd = stepCount < 0 ? 0 : stepCount;
            for (long i = rangeStart; i < rangeEnd; i++)
            {
                long red = sr + redDelta * i;
                long green = sg + greenDelta * i;
                long blue = sb + blueDelta * i;
                if (red < 0)
                {
                    red = 0;
                }
                else if (red > 255)
                {
                    red = 255;
                }

                if (green < 0)
                {
                    green = 0;
                }
                else if (green > 255)
                {
                    green = 255;
                }

                if (blue < 0)
                {
                    blue = 0;
                }
                else if (blue > 255)
                {
                    blue = 255;
                }

                spectrum.Add(Color.FromHex($"{red:x2}{green:x2}{blue:x2}"));
            }

            spectrum.Add(end);
        }

        return new Gradient(spectrum);
    }

    /// <summary>Convenience: single scalar step count (the common upstream call shape).</summary>
    public static Gradient WithSteps(IReadOnlyList<Color> stops, long steps, bool doLoop) =>
        New(stops, [steps], true, doLoop);

    /// <summary>get_color_at_fraction: first i in 1..=len with fraction &lt;= i/len.</summary>
    public Color GetColorAtFraction(double fraction)
    {
        if (!(fraction >= 0.0 && fraction <= 1.0))
        {
            throw new ArgumentException("Fraction must be 0 <= fraction <= 1.");
        }

        int len = Spectrum.Count;
        for (int i = 1; i <= len; i++)
        {
            if (fraction <= i / (double)len)
            {
                return Spectrum[i - 1];
            }
        }

        return Spectrum[Spectrum.Count - 1];
    }

    /// <summary>build_coordinate_color_mapping with upstream's insertion order per direction.</summary>
    public CoordColorMap BuildCoordinateColorMapping(
        long minRow,
        long maxRow,
        long minColumn,
        long maxColumn,
        GradientDirection direction)
    {
        if (maxRow < 1 || maxColumn < 1 || minRow < 1 || minColumn < 1)
        {
            throw new ArgumentException("max_row and max_column must be greater than 0.");
        }

        if (minRow > maxRow || minColumn > maxColumn)
        {
            throw new ArgumentException(
                "min_row and min_column must be less than or equal to max_row and max_column.");
        }

        long rowOffset = minRow - 1;
        long columnOffset = minColumn - 1;
        var mapping = new CoordColorMap();
        switch (direction)
        {
            case GradientDirection.Vertical:
                for (long row = minRow; row <= maxRow; row++)
                {
                    double fraction = (row - rowOffset) / (double)(maxRow - rowOffset);
                    Color color = GetColorAtFraction(fraction);
                    for (long column = minColumn; column <= maxColumn; column++)
                    {
                        mapping.Insert(Coord.New(column, row), color);
                    }
                }

                break;
            case GradientDirection.Horizontal:
                for (long column = minColumn; column <= maxColumn; column++)
                {
                    double fraction = (column - columnOffset) / (double)(maxColumn - columnOffset);
                    Color color = GetColorAtFraction(fraction);
                    for (long row = minRow; row <= maxRow; row++)
                    {
                        mapping.Insert(Coord.New(column, row), color);
                    }
                }

                break;
            case GradientDirection.Radial:
                for (long row = minRow; row <= maxRow; row++)
                {
                    for (long column = minColumn; column <= maxColumn; column++)
                    {
                        double distance = Geometry.FindNormalizedDistanceFromCenter(
                            minRow,
                            maxRow,
                            minColumn,
                            maxColumn,
                            Coord.New(column, row));
                        Color color = GetColorAtFraction(distance);
                        mapping.Insert(Coord.New(column, row), color);
                    }
                }

                break;
            case GradientDirection.Diagonal:
                for (long row = minRow; row <= maxRow; row++)
                {
                    for (long column = minColumn; column <= maxColumn; column++)
                    {
                        double fraction = (((row - rowOffset) * 2) + (column - columnOffset))
                            / (double)(((maxRow - rowOffset) * 2) + (maxColumn - columnOffset));
                        Color color = GetColorAtFraction(fraction);
                        mapping.Insert(Coord.New(column, row), color);
                    }
                }

                break;
        }

        return mapping;
    }
}

/// <summary>
/// graphics.shift_color_towards: float lerp with int() TRUNCATION back to hex
/// (unlike adjust_color_brightness's round()). Negative components format
/// Python-style ("-3" not two's complement) so error conditions match.
/// </summary>
public static class Graphics
{
    /// <summary>
    /// graphics.rs:361-366. <c>{i:02x}</c> for non-negative; <c>-{:01x}</c> of the
    /// magnitude when negative. C# <c>i.ToString("x2")</c> on a negative int is
    /// two's complement (<c>fffffffd</c>) and must not be used.
    /// </summary>
    public static string FormatPyHex(long i)
    {
        if (i < 0)
        {
            return "-" + (-i).ToString("x", CultureInfo.InvariantCulture);
        }

        return i.ToString("x2", CultureInfo.InvariantCulture);
    }

    public static Color ShiftColorTowards(Color color, Color targetColor, double factor)
    {
        static double Interpolate(double start, double end, double f) => start + (end - start) * f;

        (byte crB, byte cgB, byte cbB) = color.RgbInts();
        (byte trB, byte tgB, byte tbB) = targetColor.RgbInts();
        double cr = crB / 255.0;
        double cg = cgB / 255.0;
        double cb = cbB / 255.0;
        double tr = trB / 255.0;
        double tg = tgB / 255.0;
        double tb = tbB / 255.0;
        string hex = FormatPyHex(PyCompat.TruncToI64(Interpolate(cr, tr, factor) * 255.0))
            + FormatPyHex(PyCompat.TruncToI64(Interpolate(cg, tg, factor) * 255.0))
            + FormatPyHex(PyCompat.TruncToI64(Interpolate(cb, tb, factor) * 255.0));
        return Color.FromHex(hex);
    }
}
