using System.Collections.Generic;
using System.Text;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class ColorPipelineTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("unsupported SGR values ignored", UnsupportedSgrValuesIgnored);
        yield return new TestCase("malformed SGR sequences error", MalformedSgrErrors);
        yield return new TestCase("bold +8 on pending standard fg", BoldBumpsStandardFg);
        yield return new TestCase("input_colors_frequency counts overwritten cells", FrequencyCountsOverwrites);
        yield return new TestCase("always applies at parse time", AlwaysAppliesAtParse);
        yield return new TestCase("ignore leaves visuals plain", IgnoreLeavesPlain);
        yield return new TestCase("preexisting_colors_present scan", PreexistingScan);
        yield return new TestCase("always + xterm uses hex_to_xterm", AlwaysXtermHex);
        yield return new TestCase("always + nocolor drops color codes", AlwaysNoColor);
        yield return new TestCase("terminal-background-color through config", TerminalBackgroundThroughConfig);
    }

    private static void UnsupportedSgrValuesIgnored()
    {
        // dim (2), italic (3), unknown 99: values are ignored; text is kept
        var config = new TerminalConfig { IgnoreTerminalDimensions = true };
        Terminal terminal = Terminal.New("a\x1b[2;3;99mb", config);
        Harness.AssertEqual("two chars", 2, terminal.InputCharacters.Count);
        EffectCharacter b = terminal.Arena[(int)terminal.InputCharacters[1].Value];
        Harness.AssertTrue("no fg from ignored values", b.Animation.InputFgColor is null);
        Harness.AssertEqual("symbol", "b", b.InputSymbol);
    }

    private static void MalformedSgrErrors()
    {
        Harness.AssertThrows<UnsupportedAnsiException>(
            "38 without mode",
            () => Terminal.New("\x1b[38m", new TerminalConfig { IgnoreTerminalDimensions = true }));
        Harness.AssertThrows<UnsupportedAnsiException>(
            "unsupported color mode",
            () => Terminal.New("\x1b[38;3;1m", new TerminalConfig { IgnoreTerminalDimensions = true }));
        Harness.AssertThrows<UnsupportedAnsiException>(
            "38;5 truncated",
            () => Terminal.New("\x1b[38;5m", new TerminalConfig { IgnoreTerminalDimensions = true }));
    }

    private static void BoldBumpsStandardFg()
    {
        var config = new TerminalConfig
        {
            IgnoreTerminalDimensions = true,
            ExistingColorHandling = ExistingColorHandling.Always,
        };
        // 31 = standard red (xterm 1); bold bumps pending standard fg by +8 → xterm 9
        Terminal terminal = Terminal.New("\x1b[31m\x1b[1mx", config);
        EffectCharacter ch = terminal.Arena[(int)terminal.InputCharacters[0].Value];
        Harness.AssertTrue("has fg", ch.Animation.InputFgColor is not null);
        Harness.AssertEqual("bold +8", (byte)9, ch.Animation.InputFgColor!.XtermColor);
        Harness.AssertTrue("bold flag", ch.Animation.InputBold);
        string formatted = Encoding.UTF8.GetString(ch.Animation.CurrentCharacterVisual.FormattedSymbol.Bytes);
        Harness.AssertTrue("emits bold", formatted.Contains("\x1b[1m", System.StringComparison.Ordinal));
    }

    private static void FrequencyCountsOverwrites()
    {
        // 'a','b' then CUB 1 overwrites 'b' with 'c', all under SGR 31.
        // Frequency increments at creation, so the overwritten cell still counts.
        var config = new TerminalConfig { IgnoreTerminalDimensions = true };
        Terminal terminal = Terminal.New("\x1b[31mab\x1b[1Dc", config);
        Harness.AssertEqual("freq entries", 1, terminal.InputColorsFrequency.Entries.Count);
        (Color color, long count) = terminal.InputColorsFrequency.Entries[0];
        Harness.AssertTrue("xterm 1", color.Equals(Color.FromXterm(1)));
        Harness.AssertEqual("a+b+c", 3L, count);
        Harness.AssertEqual("survivors", 2, terminal.InputCharacters.Count);
    }

    private static void AlwaysAppliesAtParse()
    {
        var config = new TerminalConfig
        {
            IgnoreTerminalDimensions = true,
            ExistingColorHandling = ExistingColorHandling.Always,
        };
        Terminal terminal = Terminal.New("\x1b[31mx", config);
        EffectCharacter ch = terminal.Arena[(int)terminal.InputCharacters[0].Value];
        string formatted = Encoding.UTF8.GetString(ch.Animation.CurrentCharacterVisual.FormattedSymbol.Bytes);
        Harness.AssertTrue("has sgr", formatted.Contains("\x1b[38;2;", System.StringComparison.Ordinal));
        Harness.AssertTrue("preexisting", terminal.PreexistingColorsPresent());
    }

    private static void IgnoreLeavesPlain()
    {
        var config = new TerminalConfig
        {
            IgnoreTerminalDimensions = true,
            ExistingColorHandling = ExistingColorHandling.Ignore,
        };
        Terminal terminal = Terminal.New("\x1b[31mx", config);
        EffectCharacter ch = terminal.Arena[(int)terminal.InputCharacters[0].Value];
        Harness.AssertTrue("input fg captured", ch.Animation.InputFgColor is not null);
        string formatted = Encoding.UTF8.GetString(ch.Animation.CurrentCharacterVisual.FormattedSymbol.Bytes);
        Harness.AssertEqual("plain visual", "x", formatted);
        Harness.AssertTrue("scan still true", terminal.PreexistingColorsPresent());
    }

    private static void PreexistingScan()
    {
        var config = new TerminalConfig { IgnoreTerminalDimensions = true };
        Terminal plain = Terminal.New("hello", config);
        Harness.AssertTrue("plain false", !plain.PreexistingColorsPresent());
        Terminal colored = Terminal.New("\x1b[31mhello", config);
        Harness.AssertTrue("colored true", colored.PreexistingColorsPresent());
    }

    private static void AlwaysXtermHex()
    {
        var config = new TerminalConfig
        {
            IgnoreTerminalDimensions = true,
            ExistingColorHandling = ExistingColorHandling.Always,
            XtermColors = true,
        };
        Terminal terminal = Terminal.New("\x1b[38;2;255;0;128mx", config);
        EffectCharacter ch = terminal.Arena[(int)terminal.InputCharacters[0].Value];
        string formatted = Encoding.UTF8.GetString(ch.Animation.CurrentCharacterVisual.FormattedSymbol.Bytes);
        byte code = Hexterm.HexToXterm("FF0080");
        Harness.AssertTrue("xterm sgr", formatted.Contains($"\x1b[38;5;{code}m", System.StringComparison.Ordinal));
    }

    private static void AlwaysNoColor()
    {
        var config = new TerminalConfig
        {
            IgnoreTerminalDimensions = true,
            ExistingColorHandling = ExistingColorHandling.Always,
            NoColor = true,
        };
        Terminal terminal = Terminal.New("\x1b[1;31mx", config);
        EffectCharacter ch = terminal.Arena[(int)terminal.InputCharacters[0].Value];
        string formatted = Encoding.UTF8.GetString(ch.Animation.CurrentCharacterVisual.FormattedSymbol.Bytes);
        Harness.AssertTrue("bold kept", formatted.Contains("\x1b[1m", System.StringComparison.Ordinal));
        Harness.AssertTrue("no fg", !formatted.Contains("\x1b[38;", System.StringComparison.Ordinal));
    }

    private static void TerminalBackgroundThroughConfig()
    {
        var root = new Ttfx.Cli.RootOptions
        {
            TerminalBackgroundColor = Color.FromHex("ff0000"),
            XtermColors = true,
            NoColor = true,
            ExistingColorHandling = ExistingColorHandling.Always,
        };
        TerminalConfig config = TerminalConfig.FromRoot(root);
        Harness.AssertTrue("bg copied", config.TerminalBackgroundColor.Equals(Color.FromHex("ff0000")));
        Harness.AssertTrue("xterm copied", config.XtermColors);
        Harness.AssertTrue("nocolor copied", config.NoColor);
        Harness.AssertEqual("always copied", ExistingColorHandling.Always, config.ExistingColorHandling);
    }
}
