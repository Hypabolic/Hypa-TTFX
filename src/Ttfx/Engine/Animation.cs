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

    /// <summary>
    /// Animation._get_color_code. hex_to_xterm is issue 0005; this branch is
    /// only reached when <c>use_xterm_colors</c> is set on a hex color.
    /// </summary>
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

            // hex_to_xterm is 0005; default-option ASCII --m0-dump never hits this.
            return new Ansi.ColorCode.Rgb(color.RgbColor);
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
}
