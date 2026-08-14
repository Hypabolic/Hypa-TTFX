using System.Buffers;
using System.Text;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Handling of preexisting SGR colors in the input (TerminalConfig option).
/// </summary>
public enum ExistingColorHandling
{
    Always,
    Dynamic,
    Ignore,
}

/// <summary>
/// The precomputed ANSI string for one cell, stored as UTF-8 bytes.
/// Representation half of Rust's inline/heap union is dropped (plan §5.8);
/// the cached byte[] is the semantic half.
/// Transcribed from <c>engine/animation.rs</c>.
/// </summary>
public sealed class FormattedSymbol
{
    private readonly byte[] _bytes;

    public FormattedSymbol(byte[] bytes)
    {
        _bytes = bytes;
    }

    public static FormattedSymbol New(string text)
    {
        return new FormattedSymbol(Encoding.UTF8.GetBytes(text));
    }

    public byte[] Bytes => _bytes;

    public void AppendTo(System.Buffers.ArrayBufferWriter<byte> outBuf)
    {
        outBuf.Write(_bytes);
    }

    public void AppendTo(System.Collections.Generic.List<byte> outBuf)
    {
        outBuf.AddRange(_bytes);
    }
}

public sealed class VisualParams
{
    public bool Bold { get; set; }
    public bool Dim { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Blink { get; set; }
    public bool Reverse { get; set; }
    public bool Hidden { get; set; }
    public bool Strike { get; set; }
    public ColorPair? Colors { get; set; }
    public Ansi.ColorCode? FgColorCode { get; set; }
    public Ansi.ColorCode? BgColorCode { get; set; }
}

/// <summary>
/// animation.CharacterVisual with the formatted ANSI string precomputed.
/// </summary>
public sealed class CharacterVisual
{
    public string Symbol { get; }
    public bool Bold { get; }
    public bool Dim { get; } // stored but never emitted, faithfully
    public bool Italic { get; }
    public bool Underline { get; }
    public bool Blink { get; }
    public bool Reverse { get; }
    public bool Hidden { get; }
    public bool Strike { get; }
    public ColorPair? Colors { get; }
    public Ansi.ColorCode? FgColorCode { get; }
    public Ansi.ColorCode? BgColorCode { get; }
    public FormattedSymbol FormattedSymbol { get; }

    private static readonly StringBuilder FormatScratch = new StringBuilder();

    public CharacterVisual(string symbol, VisualParams p)
    {
        Symbol = symbol;
        Bold = p.Bold;
        Dim = p.Dim;
        Italic = p.Italic;
        Underline = p.Underline;
        Blink = p.Blink;
        Reverse = p.Reverse;
        Hidden = p.Hidden;
        Strike = p.Strike;
        Colors = p.Colors;
        FgColorCode = p.FgColorCode;
        BgColorCode = p.BgColorCode;
        // Effects rebuild visuals every frame, so the SGR string is assembled in
        // a reused scratch buffer rather than a fresh allocation per visual.
        FormatScratch.Clear();
        FormatSymbolInto(FormatScratch);
        FormattedSymbol = FormattedSymbol.New(FormatScratch.ToString());
    }

    public static CharacterVisual New(string symbol, VisualParams p) => new CharacterVisual(symbol, p);

    public static CharacterVisual Plain(string symbol) => new CharacterVisual(symbol, new VisualParams());

    /// <summary>
    /// SGR emission in upstream's fixed order; <c>dim</c> intentionally omitted;
    /// bare symbol when nothing applies.
    /// </summary>
    private void FormatSymbolInto(StringBuilder fmt)
    {
        if (Bold)
        {
            fmt.Append(Ansi.Bold);
        }

        if (Italic)
        {
            fmt.Append(Ansi.Italic);
        }

        if (Underline)
        {
            fmt.Append(Ansi.Underline);
        }

        if (Blink)
        {
            fmt.Append(Ansi.Blink);
        }

        if (Reverse)
        {
            fmt.Append(Ansi.Reverse);
        }

        if (Hidden)
        {
            fmt.Append(Ansi.Hidden);
        }

        if (Strike)
        {
            fmt.Append(Ansi.Strikethrough);
        }

        if (FgColorCode is not null)
        {
            Ansi.Fg(FgColorCode, fmt);
        }

        if (BgColorCode is not null)
        {
            Ansi.Bg(BgColorCode, fmt);
        }

        fmt.Append(Symbol);
        // Rust str::len() is bytes. Compare UTF-8 byte counts, not String.Length.
        if (Encoding.UTF8.GetByteCount(fmt.ToString()) != Encoding.UTF8.GetByteCount(Symbol))
        {
            fmt.Append(Ansi.ResetAll);
        }
    }
}

/// <summary>
/// engine/animation.py Animation: per-character animation state.
/// Scene ticking is a later issue; this is state plus SGR / appearance.
/// </summary>
public sealed class Animation
{
    public bool UseXtermColors { get; set; }
    public bool NoColor { get; set; }
    public ExistingColorHandling ExistingColorHandling { get; set; }
    public Color? InputFgColor { get; set; }
    public Color? InputBgColor { get; set; }
    public bool InputBold { get; set; }
    public CharacterVisual CurrentCharacterVisual { get; set; }

    private Animation(string inputSymbol)
    {
        UseXtermColors = false;
        NoColor = false;
        ExistingColorHandling = ExistingColorHandling.Ignore;
        InputFgColor = null;
        InputBgColor = null;
        InputBold = false;
        CurrentCharacterVisual = CharacterVisual.Plain(inputSymbol);
    }

    public static Animation New(string inputSymbol) => new Animation(inputSymbol);

    /// <summary>Animation._get_color_code.</summary>
    public Ansi.ColorCode? GetColorCode(Color? color)
    {
        if (color is null)
        {
            return null;
        }

        if (NoColor)
        {
            return null;
        }

        if (UseXtermColors)
        {
            if (color.XtermColor is byte code)
            {
                return new Ansi.ColorCode.Xterm(code);
            }

            return new Ansi.ColorCode.Xterm(Hexterm.HexToXterm(color.RgbColor));
        }

        return new Ansi.ColorCode.Rgb(color.RgbColor);
    }

    /// <summary>Animation.set_appearance.</summary>
    public void SetAppearance(
        string inputSymbol,
        bool usesInputPreexistingColors,
        string? symbol,
        ColorPair? colors)
    {
        string resolvedSymbol = symbol ?? inputSymbol;
        ColorPair resolvedColors = colors ?? new ColorPair();
        bool bold = false;
        if (ExistingColorHandling == ExistingColorHandling.Always && usesInputPreexistingColors)
        {
            resolvedColors = ColorPair.New(InputFgColor, InputBgColor);
            bold = InputBold;
        }

        Ansi.ColorCode? fgCode = GetColorCode(resolvedColors.FgColor);
        Ansi.ColorCode? bgCode = GetColorCode(resolvedColors.BgColor);
        CurrentCharacterVisual = CharacterVisual.New(
            resolvedSymbol,
            new VisualParams
            {
                Bold = bold,
                Colors = resolvedColors,
                FgColorCode = fgCode,
                BgColorCode = bgCode,
            });
    }

    /// <summary>
    /// Animation.adjust_color_brightness: hand-rolled RGB-&gt;HSL-&gt;RGB with
    /// round() (banker's) at the end — unlike shift_color_towards's truncation.
    /// </summary>
    public static Color AdjustColorBrightness(Color color, double brightness)
    {
        static double HueToRgb(double lightnessScaled, double colorIntensity, double hueValue)
        {
            if (hueValue < 0.0)
            {
                hueValue += 1.0;
            }

            if (hueValue > 1.0)
            {
                hueValue -= 1.0;
            }

            if (hueValue < 1.0 / 6.0)
            {
                return lightnessScaled + (colorIntensity - lightnessScaled) * 6.0 * hueValue;
            }

            if (hueValue < 1.0 / 2.0)
            {
                return colorIntensity;
            }

            if (hueValue < 2.0 / 3.0)
            {
                return lightnessScaled + (colorIntensity - lightnessScaled) * (2.0 / 3.0 - hueValue) * 6.0;
            }

            return lightnessScaled;
        }

        (byte r, byte g, byte b) = color.RgbInts();
        double normalizedRed = r / 255.0;
        double normalizedGreen = g / 255.0;
        double normalizedBlue = b / 255.0;

        double maxVal = PyCompat.FMax(PyCompat.FMax(normalizedRed, normalizedGreen), normalizedBlue);
        double minVal = PyCompat.FMin(PyCompat.FMin(normalizedRed, normalizedGreen), normalizedBlue);
        double lightness = (maxVal + minVal) / 2.0;

        double lightnessThreshold = 0.5;
        double hueValue;
        double saturation;
        if (maxVal == minVal)
        {
            hueValue = 0.0;
            saturation = 0.0;
        }
        else
        {
            double diff = maxVal - minVal;
            saturation = lightness > lightnessThreshold
                ? diff / (2.0 - maxVal - minVal)
                : diff / (maxVal + minVal);
            if (maxVal == normalizedRed)
            {
                hueValue = (normalizedGreen - normalizedBlue) / diff + (normalizedGreen < normalizedBlue ? 6.0 : 0.0);
            }
            else if (maxVal == normalizedGreen)
            {
                hueValue = (normalizedBlue - normalizedRed) / diff + 2.0;
            }
            else
            {
                hueValue = (normalizedRed - normalizedGreen) / diff + 4.0;
            }

            hueValue /= 6.0;
        }

        lightness = PyCompat.FMax(PyCompat.FMin(lightness * brightness, 1.0), 0.0);

        double red;
        double green;
        double blue;
        if (saturation == 0.0)
        {
            red = lightness;
            green = lightness;
            blue = lightness;
        }
        else
        {
            double colorIntensity = lightness < lightnessThreshold
                ? lightness * (1.0 + saturation)
                : lightness + saturation - lightness * saturation;
            double lightnessScaled = 2.0 * lightness - colorIntensity;
            red = HueToRgb(lightnessScaled, colorIntensity, hueValue + 1.0 / 3.0);
            green = HueToRgb(lightnessScaled, colorIntensity, hueValue);
            blue = HueToRgb(lightnessScaled, colorIntensity, hueValue - 1.0 / 3.0);
        }

        string adjusted =
            $"{PyCompat.RoundHalfEven(red * 255.0):x2}{PyCompat.RoundHalfEven(green * 255.0):x2}{PyCompat.RoundHalfEven(blue * 255.0):x2}";
        return Color.FromHex(adjusted);
    }
}
