using System;
using System.Text;

namespace Ttfx.Utils;

/// <summary>
/// ANSI escape sequences, ported from utils/ansitools.py + utils/colorterm.py.
/// Transcribed from <c>utils/ansi.rs</c>.
/// </summary>
public static class Ansi
{
    public const string DecSaveCursor = "\x1b7";
    public const string DecRestoreCursor = "\x1b8";
    public const string HideCursor = "\x1b[?25l";
    public const string ShowCursor = "\x1b[?25h";
    public const string ResetAll = "\x1b[0m";
    public const string ClearToEndOfScreen = "\x1b[0J";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Italic = "\x1b[3m";
    public const string Underline = "\x1b[4m";
    public const string Blink = "\x1b[5m";
    public const string Reverse = "\x1b[7m";
    public const string Hidden = "\x1b[8m";
    public const string Strikethrough = "\x1b[9m";

    public static readonly byte[] ResetAllBytes = "\x1b[0m"u8.ToArray();
    public static readonly byte[] BoldBytes = "\x1b[1m"u8.ToArray();
    public static readonly byte[] ItalicBytes = "\x1b[3m"u8.ToArray();
    public static readonly byte[] UnderlineBytes = "\x1b[4m"u8.ToArray();
    public static readonly byte[] BlinkBytes = "\x1b[5m"u8.ToArray();
    public static readonly byte[] ReverseBytes = "\x1b[7m"u8.ToArray();
    public static readonly byte[] HiddenBytes = "\x1b[8m"u8.ToArray();
    public static readonly byte[] StrikethroughBytes = "\x1b[9m"u8.ToArray();

    /// <summary>
    /// A resolved color code ready for SGR emission: hex string =&gt; 24-bit, int =&gt; 8-bit.
    /// Mirrors the str|int union threaded through colorterm/animation upstream.
    /// </summary>
    public abstract class ColorCode : IEquatable<ColorCode>
    {
        private ColorCode()
        {
        }

        public sealed class Rgb : ColorCode
        {
            public string Hex { get; }

            public Rgb(string hex)
            {
                Hex = hex;
            }

            public override bool Equals(ColorCode? other) => other is Rgb rgb && Hex == rgb.Hex;

            public override int GetHashCode() => Hex.GetHashCode(StringComparison.Ordinal);
        }

        public sealed class Xterm : ColorCode
        {
            public byte Code { get; }

            public Xterm(byte code)
            {
                Code = code;
            }

            public override bool Equals(ColorCode? other) => other is Xterm x && Code == x.Code;

            public override int GetHashCode() => Code.GetHashCode();
        }

        public abstract bool Equals(ColorCode? other);

        public override bool Equals(object? obj) => obj is ColorCode other && Equals(other);

        public override int GetHashCode() => base.GetHashCode();
    }

    /// <summary>
    /// Decimal digits of a byte, without going through format machinery.
    /// </summary>
    public static void PushDecimal(StringBuilder outBuf, byte value)
    {
        if (value >= 100)
        {
            outBuf.Append((char)('0' + value / 100));
        }

        if (value >= 10)
        {
            outBuf.Append((char)('0' + (value / 10) % 10));
        }

        outBuf.Append((char)('0' + value % 10));
    }

    /// <summary>
    /// colorterm._color: fg selector 38, bg selector 48.
    /// </summary>
    public static void SgrColor(ColorCode code, byte location, StringBuilder outBuf)
    {
        outBuf.Append("\x1b[");
        PushDecimal(outBuf, location);
        switch (code)
        {
            case ColorCode.Rgb rgb:
            {
                byte[] channels = Hexterm.ParseRgb(rgb.Hex);
                byte r = channels[0];
                byte g = channels[1];
                byte b = channels[2];
                outBuf.Append(";2;");
                PushDecimal(outBuf, r);
                outBuf.Append(';');
                PushDecimal(outBuf, g);
                outBuf.Append(';');
                PushDecimal(outBuf, b);
                break;
            }
            case ColorCode.Xterm xterm:
                outBuf.Append(";5;");
                PushDecimal(outBuf, xterm.Code);
                break;
        }

        outBuf.Append('m');
    }

    public static void Fg(ColorCode code, StringBuilder outBuf) => SgrColor(code, 38, outBuf);

    public static void Bg(ColorCode code, StringBuilder outBuf) => SgrColor(code, 48, outBuf);
}
