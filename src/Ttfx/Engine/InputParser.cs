using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ttfx.Utils;

namespace Ttfx.Engine;

public sealed class UnsupportedAnsiException : Exception
{
    public string Sequence { get; }

    public UnsupportedAnsiException(string sequence)
        : base("unsupported ansi")
    {
        Sequence = sequence;
    }
}

/// <summary>
/// Insertion-ordered Color -&gt; count map (upstream: dict[Color, int]). Iteration
/// order is behavior for get_input_colors; the population is small, linear scan.
/// </summary>
public sealed class ColorFrequency
{
    public List<(Color Color, long Count)> Entries { get; } = new List<(Color, long)>();

    public void Increment(Color color)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Color.Equals(color))
            {
                Entries[i] = (Entries[i].Color, Entries[i].Count + 1);
                return;
            }
        }

        Entries.Add((color, 1));
    }
}

internal sealed class ActiveState
{
    public string FgSequence = ""; // "" = none, like upstream's active_sequences
    public string BgSequence = "";
    public Color? FgColor;
    public Color? BgColor;
    public bool Bold;
    public long? StandardFgParameter;
}

/// <summary>
/// Input preprocessing: the mini terminal emulator from Terminal._preprocess_input_data.
///
/// Walks the input codepoint-by-codepoint (one rune = one cell, faithfully — no
/// wcwidth upstream), tracking SGR color state and cursor movement, producing
/// rows of arena character ids. Everything here, including which malformed
/// sequences error vs. get silently ignored, transcribes terminal.py:604-862.
/// Transcribed from <c>engine/input.rs</c>.
/// </summary>
public sealed class Preprocessor
{
    public List<EffectCharacter> Arena { get; }
    public uint NextCharacterId { get; set; }
    public ColorFrequency InputColorsFrequency { get; }
    public TerminalConfig Config { get; }

    public Preprocessor(
        List<EffectCharacter> arena,
        uint nextCharacterId,
        ColorFrequency inputColorsFrequency,
        TerminalConfig config)
    {
        Arena = arena;
        NextCharacterId = nextCharacterId;
        InputColorsFrequency = inputColorsFrequency;
        Config = config;
    }

    /// <summary>Returns rows of character ids (top row first, as parsed).</summary>
    public List<List<CharId>> Preprocess(string inputData)
    {
        // input.rs:51 chars().collect() → Rune[]; one rune = one cell
        var chars = new List<Rune>();
        foreach (Rune rune in inputData.EnumerateRunes())
        {
            chars.Add(rune);
        }

        var screen = new Dictionary<(long, long), CharId>();
        var state = new ActiveState();
        long row = 0;
        long column = 0;
        long maxRow = 0;
        long maxColumn = 0;
        int i = 0;

        while (i < chars.Count)
        {
            if (chars[i].Value == 0x1B)
            {
                int? end = MatchEscapeSequence(chars, i);
                if (end is null)
                {
                    throw new UnsupportedAnsiException(RuneToString(chars[i]));
                }

                string sequence = RunesToString(chars, i, end.Value);
                if (sequence.StartsWith("\x1b[", StringComparison.Ordinal))
                {
                    if (!SplitCsi(sequence, out string parameters, out string intermediates, out char finalByte))
                    {
                        throw new UnsupportedAnsiException(sequence);
                    }

                    if (finalByte == 'm')
                    {
                        ApplySgrSequence(sequence, parameters, state);
                    }
                    else if (IsSupportedPrivateModeSequence(sequence))
                    {
                        // ignored: cursor show/hide, autowrap on/off
                    }
                    else
                    {
                        (long newRow, long newColumn) = ApplyCursorSequence(
                            sequence, parameters, intermediates, finalByte, row, column);
                        row = newRow;
                        column = newColumn;
                        maxRow = Math.Max(maxRow, row);
                        maxColumn = Math.Max(maxColumn, column);
                    }
                }
                else
                {
                    throw new UnsupportedAnsiException(sequence);
                }

                i = end.Value;
            }
            else if (chars[i].Value == '\n')
            {
                row += 1;
                column = 0;
                maxRow = Math.Max(maxRow, row);
                i += 1;
            }
            else if (chars[i].Value == '\r')
            {
                column = 0;
                i += 1;
            }
            else
            {
                Rune symbolRune;
                long count;
                if (chars[i].Value == '\t')
                {
                    symbolRune = new Rune(' ');
                    count = Config.TabWidth - (column % Config.TabWidth);
                }
                else
                {
                    symbolRune = chars[i];
                    count = 1;
                }

                string symbol = RuneToString(symbolRune);
                for (long n = 0; n < count; n++)
                {
                    CharId id = BuildCharacter(symbol, state);
                    screen[(row, column)] = id;
                    maxRow = Math.Max(maxRow, row);
                    maxColumn = Math.Max(maxColumn, column);
                    column += 1;
                }

                i += 1;
            }
        }

        var emptyState = new ActiveState();
        var characters = new List<List<CharId>>();
        for (long screenRow = 0; screenRow <= maxRow; screenRow++)
        {
            var line = new List<CharId>();
            for (long screenColumn = 0; screenColumn <= maxColumn; screenColumn++)
            {
                CharId id;
                if (screen.TryGetValue((screenRow, screenColumn), out CharId existing))
                {
                    id = existing;
                }
                else
                {
                    id = BuildCharacter(" ", emptyState);
                }

                line.Add(id);
            }

            while (line.Count > 0)
            {
                EffectCharacter ch = Arena[(int)line[line.Count - 1].Value];
                if (ch.InputSymbol == " "
                    && ch.Animation.InputFgColor is null
                    && ch.Animation.InputBgColor is null)
                {
                    line.RemoveAt(line.Count - 1);
                }
                else
                {
                    break;
                }
            }

            characters.Add(line);
        }

        while (characters.Count > 0 && characters[characters.Count - 1].Count == 0)
        {
            characters.RemoveAt(characters.Count - 1);
        }

        if (characters.Count == 0)
        {
            // Faithful: the fallback character carries the END-of-input active state.
            CharId id = BuildCharacter(" ", state);
            characters.Add(new List<CharId> { id });
        }

        return characters;
    }

    /// <summary>
    /// build_character: allocates an id (even for characters later discarded),
    /// captures active colors, bumps the color frequency at CREATION time (even
    /// if a later cursor write overwrites the cell — see plan.md §5.13).
    /// </summary>
    private CharId BuildCharacter(string symbol, ActiveState state)
    {
        var ch = new EffectCharacter(NextCharacterId, symbol, 0, 0);
        NextCharacterId += 1;
        // fg first, then bg — upstream dict iteration order over active_sequences
        if (state.FgSequence.Length != 0)
        {
            if (state.FgColor is Color color)
            {
                ch.InputAnsiFgSequence = state.FgSequence;
                InputColorsFrequency.Increment(color);
                ch.Animation.InputFgColor = color;
            }
        }

        if (state.BgSequence.Length != 0)
        {
            if (state.BgColor is Color color)
            {
                ch.InputAnsiBgSequence = state.BgSequence;
                InputColorsFrequency.Increment(color);
                ch.Animation.InputBgColor = color;
            }
        }

        ch.Animation.InputBold = state.Bold;
        ch.Animation.NoColor = Config.NoColor;
        ch.Animation.UseXtermColors = Config.XtermColors;
        ch.Animation.ExistingColorHandling = Config.ExistingColorHandling;
        ch.UsesInputPreexistingColors = true;
        if (ch.Animation.ExistingColorHandling == ExistingColorHandling.Always)
        {
            string inputSymbol = ch.InputSymbol;
            ch.Animation.SetAppearance(inputSymbol, true, null, null);
        }

        var id = new CharId((uint)Arena.Count);
        Arena.Add(ch);
        return id;
    }

    private void ApplySgrSequence(string sequence, string paramsText, ActiveState state)
    {
        List<long> parameters = ParseCsiParameters(paramsText);
        if (parameters.Count == 0)
        {
            parameters = new List<long> { 0 };
        }

        int idx = 0;
        while (idx < parameters.Count)
        {
            long parameter = parameters[idx];
            switch (parameter)
            {
                case 0:
                    state.FgSequence = "";
                    state.BgSequence = "";
                    state.FgColor = null;
                    state.BgColor = null;
                    state.Bold = false;
                    state.StandardFgParameter = null;
                    break;
                case 1:
                    state.Bold = true;
                    if (state.StandardFgParameter is long p1)
                    {
                        state.FgColor = XtermColor(p1 - 30 + 8);
                    }

                    break;
                case 22:
                    state.Bold = false;
                    if (state.StandardFgParameter is long p22)
                    {
                        state.FgColor = XtermColor(p22 - 30);
                    }

                    break;
                case 39:
                    state.FgSequence = "";
                    state.FgColor = null;
                    state.StandardFgParameter = null;
                    break;
                case 49:
                    state.BgSequence = "";
                    state.BgColor = null;
                    break;
                case >= 30 and <= 37:
                {
                    Color color = XtermColor(parameter - 30 + (state.Bold ? 8 : 0));
                    state.FgSequence = $"\x1b[{parameter.ToString(CultureInfo.InvariantCulture)}m";
                    state.FgColor = color;
                    state.StandardFgParameter = parameter;
                    break;
                }
                case >= 90 and <= 97:
                {
                    Color color = XtermColor(parameter - 90 + 8);
                    state.FgSequence = $"\x1b[{parameter.ToString(CultureInfo.InvariantCulture)}m";
                    state.FgColor = color;
                    state.StandardFgParameter = null;
                    break;
                }
                case >= 40 and <= 47:
                {
                    Color color = XtermColor(parameter - 40);
                    state.BgSequence = $"\x1b[{parameter.ToString(CultureInfo.InvariantCulture)}m";
                    state.BgColor = color;
                    break;
                }
                case >= 100 and <= 107:
                {
                    Color color = XtermColor(parameter - 100 + 8);
                    state.BgSequence = $"\x1b[{parameter.ToString(CultureInfo.InvariantCulture)}m";
                    state.BgColor = color;
                    break;
                }
                case 38:
                case 48:
                {
                    if (idx + 1 >= parameters.Count)
                    {
                        throw new UnsupportedAnsiException(sequence);
                    }

                    bool isFg = parameter == 38;
                    long selector = parameter;
                    long colorMode = parameters[idx + 1];
                    string normalizedSequence;
                    Color color;
                    switch (colorMode)
                    {
                        case 5:
                            if (idx + 2 >= parameters.Count)
                            {
                                throw new UnsupportedAnsiException(sequence);
                            }

                            long code = parameters[idx + 2];
                            color = XtermColor(code);
                            idx += 2;
                            normalizedSequence =
                                $"\x1b[{selector.ToString(CultureInfo.InvariantCulture)};5;{code.ToString(CultureInfo.InvariantCulture)}m";
                            break;
                        case 2:
                            if (idx + 4 >= parameters.Count)
                            {
                                throw new UnsupportedAnsiException(sequence);
                            }

                            var hex = new StringBuilder();
                            for (int o = 2; o < 5; o++)
                            {
                                hex.Append(parameters[idx + o].ToString("X2", CultureInfo.InvariantCulture));
                            }

                            try
                            {
                                color = Color.FromHex(hex.ToString());
                            }
                            catch (ArgumentException ex)
                            {
                                throw new EngineException(ex.Message);
                            }

                            (byte r, byte g, byte b) = color.RgbInts();
                            idx += 4;
                            normalizedSequence =
                                $"\x1b[{selector.ToString(CultureInfo.InvariantCulture)};2;{r.ToString(CultureInfo.InvariantCulture)};{g.ToString(CultureInfo.InvariantCulture)};{b.ToString(CultureInfo.InvariantCulture)}m";
                            break;
                        default:
                            throw new UnsupportedAnsiException(sequence);
                    }

                    if (isFg)
                    {
                        state.FgSequence = normalizedSequence;
                        state.FgColor = color;
                        state.StandardFgParameter = null;
                    }
                    else
                    {
                        state.BgSequence = normalizedSequence;
                        state.BgColor = color;
                    }

                    break;
                }
                default:
                    // Faithful: any other SGR parameter value is silently ignored
                    // (the upstream loop has no fallback error branch).
                    break;
            }

            idx += 1;
        }
    }

    internal static Color XtermColor(long code)
    {
        // Upstream Color(int) raises ValueError outside 0..=255; that error is not
        // an UnsupportedAnsiSequenceError but still aborts the run.
        if (code >= 0 && code <= 255)
        {
            return Color.FromXterm((byte)code);
        }

        throw new EngineException($"invalid xterm color code in input: {code.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>parse_csi_parameters: only digits and ';' allowed; empty fields are 0.</summary>
    internal static List<long> ParseCsiParameters(string parameters)
    {
        foreach (Rune c in parameters.EnumerateRunes())
        {
            // Rust char::is_ascii_digit — not Rune.IsDigit (Unicode digits).
            bool asciiDigit = c.Value >= '0' && c.Value <= '9';
            if (!asciiDigit && c.Value != ';')
            {
                throw new UnsupportedAnsiException("\x1b[" + parameters);
            }
        }

        if (parameters.Length == 0)
        {
            return new List<long>();
        }

        var result = new List<long>();
        foreach (string p in parameters.Split(';'))
        {
            if (p.Length == 0)
            {
                result.Add(0);
            }
            else
            {
                result.Add(long.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture));
            }
        }

        return result;
    }

    internal static long DefaultParameter(IReadOnlyList<long> parameters)
    {
        if (parameters.Count == 0)
        {
            return 1;
        }

        return Math.Max(parameters[0], 1);
    }

    internal static bool IsSupportedPrivateModeSequence(string sequence)
    {
        return sequence is "\x1b[?25h" or "\x1b[?25l" or "\x1b[?7h" or "\x1b[?7l";
    }

    internal static (long Row, long Column) ApplyCursorSequence(
        string sequence,
        string paramsText,
        string intermediates,
        char finalByte,
        long row,
        long column)
    {
        if (intermediates.Length != 0)
        {
            throw new UnsupportedAnsiException(sequence);
        }

        if (paramsText.StartsWith('?'))
        {
            throw new UnsupportedAnsiException(sequence);
        }

        List<long> parameters = ParseCsiParameters(paramsText);
        switch (finalByte)
        {
            case 'A':
                row -= DefaultParameter(parameters);
                break;
            case 'B':
                row += DefaultParameter(parameters);
                break;
            case 'C':
                column += DefaultParameter(parameters);
                break;
            case 'D':
                column -= DefaultParameter(parameters);
                break;
            case 'E':
                row += DefaultParameter(parameters);
                column = 0;
                break;
            case 'F':
                row -= DefaultParameter(parameters);
                column = 0;
                break;
            case 'G':
                column = DefaultParameter(parameters) - 1;
                break;
            case 'H':
            case 'f':
                row = DefaultParameter(parameters) - 1;
                column = parameters.Count > 1 && parameters[1] != 0 ? parameters[1] - 1 : 0;
                break;
            default:
                throw new UnsupportedAnsiException(sequence);
        }

        return (Math.Max(row, 0), Math.Max(column, 0));
    }

    /// <summary>
    /// Emulates upstream's ansi_escape_sequence_pattern.match at a position, with
    /// the same alternation order: OSC, CSI, then `\x1b.` (any char except newline).
    /// Returns the exclusive end index of the match.
    /// </summary>
    internal static int? MatchEscapeSequence(IReadOnlyList<Rune> chars, int start)
    {
        if (chars[start].Value != 0x1B)
        {
            throw new EngineInvariantException("match_escape_sequence start is not ESC");
        }

        // OSC: \x1b\] [^\x07]* (\x07 | \x1b\\)  — greedy class run, longest match first
        if (start + 1 < chars.Count && chars[start + 1].Value == ']')
        {
            int runStart = start + 2;
            int t = runStart;
            while (t < chars.Count && chars[t].Value != 0x07)
            {
                t += 1;
            }

            if (t < chars.Count)
            {
                // class run ends right before \x07: longest match consumes it
                return t + 1;
            }

            // no BEL: backtrack for the rightmost \x1b\\ terminator inside the run
            int p = chars.Count;
            while (p >= runStart + 2)
            {
                if (chars[p - 2].Value == 0x1B && chars[p - 1].Value == '\\')
                {
                    return p;
                }

                p -= 1;
            }

            // fall through to the remaining alternatives, like regex alternation
        }

        // CSI: \x1b\[ [0-?]* [ -/]* [@-~]
        if (start + 1 < chars.Count && chars[start + 1].Value == '[')
        {
            int t = start + 2;
            while (t < chars.Count && chars[t].Value >= 0x30 && chars[t].Value <= 0x3F)
            {
                t += 1;
            }

            while (t < chars.Count && chars[t].Value >= 0x20 && chars[t].Value <= 0x2F)
            {
                t += 1;
            }

            if (t < chars.Count && chars[t].Value >= 0x40 && chars[t].Value <= 0x7E)
            {
                return t + 1;
            }

            // no valid final byte: fall through to \x1b.
        }

        // \x1b. — '.' does not match newline
        if (start + 1 < chars.Count && chars[start + 1].Value != '\n')
        {
            return start + 2;
        }

        return null;
    }

    /// <summary>
    /// splits a full CSI sequence into (params, intermediates, final) like
    /// csi_sequence_pattern.fullmatch; false if it isn't a well-formed CSI sequence.
    /// </summary>
    internal static bool SplitCsi(string sequence, out string parameters, out string intermediates, out char finalByte)
    {
        parameters = "";
        intermediates = "";
        finalByte = '\0';
        var chars = new List<Rune>();
        foreach (Rune rune in sequence.EnumerateRunes())
        {
            chars.Add(rune);
        }

        if (chars.Count < 3 || chars[0].Value != 0x1B || chars[1].Value != '[')
        {
            return false;
        }

        int t = 2;
        int paramsStart = t;
        while (t < chars.Count && chars[t].Value >= 0x30 && chars[t].Value <= 0x3F)
        {
            t += 1;
        }

        parameters = RunesToString(chars, paramsStart, t);
        int interStart = t;
        while (t < chars.Count && chars[t].Value >= 0x20 && chars[t].Value <= 0x2F)
        {
            t += 1;
        }

        intermediates = RunesToString(chars, interStart, t);
        if (t != chars.Count - 1)
        {
            return false;
        }

        finalByte = (char)chars[t].Value;
        if (finalByte < 0x40 || finalByte > 0x7E)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 0003 entry: reject known-unsupported CSI so stream routing is testable.
    /// Now the real preprocessor.
    /// </summary>
    public static void RejectUnsupported(string input)
    {
        var arena = new List<EffectCharacter>();
        var freq = new ColorFrequency();
        var config = new TerminalConfig();
        var preprocessor = new Preprocessor(arena, 0, freq, config);
        preprocessor.Preprocess(input);
    }

    private static string RuneToString(Rune rune)
    {
        return rune.ToString();
    }

    private static string RunesToString(IReadOnlyList<Rune> chars, int start, int end)
    {
        var sb = new StringBuilder();
        for (int i = start; i < end; i++)
        {
            sb.Append(chars[i].ToString());
        }

        return sb.ToString();
    }
}

/// <summary>
/// 0003 name kept so Program and tests still call <c>InputParser.RejectUnsupported</c>.
/// </summary>
public static class InputParser
{
    public static void RejectUnsupported(string input) => Preprocessor.RejectUnsupported(input);
}
