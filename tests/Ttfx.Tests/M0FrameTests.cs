using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class M0FrameTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("visible extents differ from canvas", VisibleDiffersFromCanvas);
        yield return new TestCase("sgr order dim never emitted", SgrOrderDimOmitted);
        yield return new TestCase("character_id gaps overwrite+whitespace", CharacterIdGaps);
        yield return new TestCase("tiocgwinsz matches C probe", TiocgwinszMatchesCProbe);
        yield return new TestCase("swap_remove hides without shifting", SwapRemoveVisibility);
        yield return new TestCase("reject unsupported ansi still throws", RejectUnsupportedAnsi);
    }

    private static void VisibleDiffersFromCanvas()
    {
        string? oldCols = Environment.GetEnvironmentVariable("COLUMNS");
        string? oldLines = Environment.GetEnvironmentVariable("LINES");
        try
        {
            Environment.SetEnvironmentVariable("COLUMNS", "10");
            Environment.SetEnvironmentVariable("LINES", "5");
            var config = new TerminalConfig
            {
                CanvasWidth = 20,
                CanvasHeight = 10,
                IgnoreTerminalDimensions = false,
            };
            Terminal terminal = Terminal.New("hello", config);
            Harness.AssertEqual("canvas height", 10L, terminal.Canvas.Height);
            Harness.AssertEqual("canvas width", 20L, terminal.Canvas.Width);
            Harness.AssertEqual("visible_top", 5L, terminal.VisibleTop);
            Harness.AssertEqual("visible_right", 10L, terminal.VisibleRight);
            foreach (CharId id in terminal.CharacterByInputCoord.Values)
            {
                terminal.SetCharacterVisibility(id, true);
            }

            ReadOnlyMemory<byte> frame = terminal.GetFormattedOutputString();
            string text = Encoding.UTF8.GetString(frame.Span);
            string[] rows = text.Split('\n');
            Harness.AssertEqual("frame rows = visible_top", 5, rows.Length);
            Harness.AssertEqual("frame cols = visible_right", 10, rows[0].Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLUMNS", oldCols);
            Environment.SetEnvironmentVariable("LINES", oldLines);
        }
    }

    private static void SgrOrderDimOmitted()
    {
        var vis = CharacterVisual.New(
            "x",
            new VisualParams
            {
                Bold = true,
                Dim = true,
                Italic = true,
                Underline = true,
                Blink = true,
                Reverse = true,
                Hidden = true,
                Strike = true,
                FgColorCode = new Ansi.ColorCode.Xterm(1),
                BgColorCode = new Ansi.ColorCode.Xterm(2),
            });

        string formatted = Encoding.UTF8.GetString(vis.FormattedSymbol.Bytes);
        Harness.AssertTrue("dim stored", vis.Dim);
        Harness.AssertTrue("dim never emitted", !formatted.Contains("\x1b[2m", StringComparison.Ordinal));
        string expected =
            "\x1b[1m\x1b[3m\x1b[4m\x1b[5m\x1b[7m\x1b[8m\x1b[9m\x1b[38;5;1m\x1b[48;5;2mx\x1b[0m";
        Harness.AssertEqual("sgr order", expected, formatted);

        var plain = CharacterVisual.Plain("y");
        Harness.AssertEqual("bare symbol", "y", Encoding.UTF8.GetString(plain.FormattedSymbol.Bytes));
    }

    private static void CharacterIdGaps()
    {
        // 'a','b' then CSI CUB 1 overwrites 'b' with 'c', then trailing spaces
        // that are popped. Ids are allocated for the overwritten and popped cells.
        string input = "ab\x1b[1Dc   ";
        var config = new TerminalConfig { IgnoreTerminalDimensions = true };
        Terminal terminal = Terminal.New(input, config);
        var ids = new List<uint>();
        foreach (CharId id in terminal.InputCharacters)
        {
            ids.Add(terminal.Arena[(int)id.Value].CharacterId);
        }

        Harness.AssertTrue("at least two survivors", ids.Count >= 2);
        Harness.AssertEqual("first survivor id", 0u, ids[0]);
        Harness.AssertEqual("overwritten cell leaves a gap", 2u, ids[1]);
        Harness.AssertTrue("list index != character_id", ids[1] != 1);
        bool sawGap = false;
        for (int i = 1; i < ids.Count; i++)
        {
            if (ids[i] != ids[i - 1] + 1)
            {
                sawGap = true;
            }
        }

        Harness.AssertTrue("explicit CharacterId field has gaps", sawGap);
    }

    private static void TiocgwinszMatchesCProbe()
    {
        string root = Harness.FindRepoRoot();
        string probe = Path.Combine(root, "tools", "parity", "tiocgwinsz_probe.c");
        string exe = Path.Combine(Path.GetTempPath(), "hypa-tiocgwinsz-probe");
        var compile = Process.Start(new ProcessStartInfo
        {
            FileName = "cc",
            Arguments = $"-o \"{exe}\" \"{probe}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        compile!.WaitForExit();
        Harness.AssertEqual("cc exit", 0, compile.ExitCode);
        var run = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        string output = run!.StandardOutput.ReadToEnd();
        run.WaitForExit();
        Harness.AssertTrue("probe TIOCGWINSZ", output.Contains($"TIOCGWINSZ=0x{PosixTerminal.DarwinTiocgwinsz:x}", StringComparison.Ordinal));
        Harness.AssertTrue("probe sizeof", output.Contains($"sizeof_winsize={PosixTerminal.WinSizeBytes}", StringComparison.Ordinal));
    }

    private static void SwapRemoveVisibility()
    {
        var config = new TerminalConfig { IgnoreTerminalDimensions = true };
        Terminal terminal = Terminal.New("ab", config);
        var ids = new List<CharId>(terminal.CharacterByInputCoord.Values);
        foreach (CharId id in ids)
        {
            terminal.SetCharacterVisibility(id, true);
        }

        CharId first = terminal.InputCharacters[0];
        terminal.SetCharacterVisibility(first, false);
        Harness.AssertTrue("hidden", !terminal.Arena[(int)first.Value].IsVisible);
        terminal.SetCharacterVisibility(first, true);
        Harness.AssertTrue("shown again", terminal.Arena[(int)first.Value].IsVisible);
    }

    private static void RejectUnsupportedAnsi()
    {
        Harness.AssertThrows<UnsupportedAnsiException>(
            "2J",
            () => InputParser.RejectUnsupported("a\x1b[2Jb"));
    }
}
