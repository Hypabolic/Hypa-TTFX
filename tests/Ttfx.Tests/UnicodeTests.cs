using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ttfx.Engine;
using Ttfx.Utils;
using Path = System.IO.Path;

namespace Ttfx.Tests;

internal static class UnicodeTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("parser cells are runes not chars", ParserCellsAreRunes);
        yield return new TestCase("unicode fixture one codepoint one cell", FixtureCells);
        yield return new TestCase("astral codepoint to {:08b}", AstralBinary);
        yield return new TestCase("to_digit ASCII only", ToDigitAsciiOnly);
        yield return new TestCase("symbol one rune count", RuneCountHelper);
    }

    /// <summary>
    /// A + U+1F600 + e + U+0301 + Z — 5 scalars. A UTF-16 walk yields 6
    /// (surrogate pair). A grapheme walk yields 4 (é is one cluster).
    /// </summary>
    private const string AstralAndCombining = "A\U0001F600e\u0301Z";

    private static void ParserCellsAreRunes()
    {
        var config = new TerminalConfig { IgnoreTerminalDimensions = true };
        Terminal terminal = Terminal.New(AstralAndCombining, config);

        Harness.AssertEqual("rune cells", 5, terminal.InputCharacters.Count);
        Harness.AssertEqual("UTF-16 units would be 6", 6, AstralAndCombining.Length);
        Harness.AssertEqual("grapheme clusters would be 4", 4, GraphemeCount(AstralAndCombining));

        string[] expected = ["A", "\U0001F600", "e", "\u0301", "Z"];
        for (int i = 0; i < expected.Length; i++)
        {
            EffectCharacter ch = terminal.Arena[(int)terminal.InputCharacters[i].Value];
            Harness.AssertEqual($"cell {i} symbol", expected[i], ch.InputSymbol);
            Harness.AssertEqual($"cell {i} rune count", 1, Unicode.RuneCount(ch.InputSymbol));
        }

        Harness.AssertEqual("astral InputSymbol is two UTF-16 chars", 2, expected[1].Length);
        Harness.AssertTrue("astral is not a lone surrogate", Rune.TryGetRuneAt(expected[1], 0, out Rune grinning));
        Harness.AssertEqual("astral scalar", 0x1F600, grinning.Value);
    }

    private static void FixtureCells()
    {
        string path = Path.Combine(Harness.FindRepoRoot(), "tools", "parity", "inputs", "unicode.txt");
        string text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        // File may end with a newline; parser treats that as a row break.
        string body = text.TrimEnd('\n').TrimEnd('\r');
        var config = new TerminalConfig { IgnoreTerminalDimensions = true };
        Terminal terminal = Terminal.New(body, config);

        var symbols = new List<string>();
        foreach (CharId id in terminal.InputCharacters)
        {
            symbols.Add(terminal.Arena[(int)id.Value].InputSymbol);
        }

        Harness.AssertTrue("has astral grinning face", symbols.Contains("\U0001F600"));
        Harness.AssertTrue("has combining acute as its own cell", symbols.Contains("\u0301"));
        Harness.AssertTrue("has combining diaeresis as its own cell", symbols.Contains("\u0308"));
        Harness.AssertTrue("has gothic hwair (U+10348)", symbols.Contains("\U00010348"));
        foreach (string symbol in symbols)
        {
            Harness.AssertEqual($"fixture cell is one rune: {Escape(symbol)}", 1, Unicode.RuneCount(symbol));
        }
    }

    private static void AstralBinary()
    {
        var grinning = new Rune(0x1F600);
        string binary = Unicode.CodepointToBinary(grinning);
        // 0x1F600 = 128512 = 11111011000000000; {:08b} pads to at least 8.
        Harness.AssertEqual("astral {:08b}", "11111011000000000", binary);
        Harness.AssertEqual("symbol wrapper", binary, Unicode.SymbolToBinary("\U0001F600"));

        // A UTF-16 char walk yields the high surrogate 0xD83D = 55357.
        string highSurrogateBinary = 0xD83D.ToString("B8", CultureInfo.InvariantCulture);
        Harness.AssertEqual("high-surrogate trap", "1101100000111101", highSurrogateBinary);
        Harness.AssertTrue("must not use high surrogate", binary != highSurrogateBinary);

        Harness.AssertEqual("ASCII A padded", "01000001", Unicode.CodepointToBinary(new Rune('A')));
        Harness.AssertThrows<EngineInvariantException>("empty symbol", () => Unicode.SymbolToBinary(""));
    }

    private static void ToDigitAsciiOnly()
    {
        Harness.AssertEqual("0", 0u, Unicode.ToDigit10(new Rune('0')).GetValueOrDefault());
        Harness.AssertTrue("0 is Some", Unicode.ToDigit10(new Rune('0')) is not null);
        Harness.AssertEqual("9", 9u, Unicode.ToDigit10(new Rune('9')).GetValueOrDefault());
        Harness.AssertTrue("A radix 10 is None", Unicode.ToDigit10(new Rune('A')) is null);
        Harness.AssertEqual("A radix 16", 10u, Unicode.ToDigit(new Rune('A'), 16).GetValueOrDefault());
        Harness.AssertEqual("f radix 16", 15u, Unicode.ToDigit(new Rune('f'), 16).GetValueOrDefault());

        var fullwidthThree = new Rune('３'); // U+FF13
        Harness.AssertTrue("fullwidth ３ is None", Unicode.ToDigit10(fullwidthThree) is null);
        Harness.AssertEqual("GetNumericValue trap", 3.0, char.GetNumericValue('３'));

        var superscriptThree = new Rune('³'); // U+00B3
        Harness.AssertTrue("superscript ³ is None", Unicode.ToDigit10(superscriptThree) is null);

        var arabicThree = new Rune('٣'); // U+0663
        Harness.AssertTrue("arabic-indic ٣ is None", Unicode.ToDigit10(arabicThree) is null);

        Harness.AssertEqual("first char digit", 3L, Unicode.FirstCharDigit("3-path"));
        Harness.AssertThrows<EngineInvariantException>("non-digit path", () => Unicode.FirstCharDigit("path"));
        Harness.AssertThrows<EngineInvariantException>("empty path", () => Unicode.FirstCharDigit(""));
        Harness.AssertThrows<EngineInvariantException>("radix 1", () => Unicode.ToDigit(new Rune('0'), 1));
        Harness.AssertThrows<EngineInvariantException>("radix 37", () => Unicode.ToDigit(new Rune('0'), 37));
    }

    private static void RuneCountHelper()
    {
        Harness.AssertEqual("empty", 0, Unicode.RuneCount(""));
        Harness.AssertEqual("ascii", 1, Unicode.RuneCount("x"));
        Harness.AssertEqual("astral", 1, Unicode.RuneCount("\U0001F600"));
        Harness.AssertEqual("astral+ascii", 2, Unicode.RuneCount("A\U0001F600"));
        Harness.AssertEqual("combining pair", 2, Unicode.RuneCount("e\u0301"));
    }

    private static int GraphemeCount(string s)
    {
        int n = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(s);
        while (enumerator.MoveNext())
        {
            n += 1;
        }

        return n;
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (Rune r in s.EnumerateRunes())
        {
            sb.Append($"U+{r.Value:X4}");
        }

        return sb.ToString();
    }
}
