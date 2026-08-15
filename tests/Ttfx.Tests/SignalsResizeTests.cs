using System;
using System.Collections.Generic;
using System.Threading;
using Ttfx.Engine;

namespace Ttfx.Tests;

/// <summary>
/// Four resize_settled suppression checks, individually.
/// Transcribed contract from <c>terminal.rs</c> 622-640.
/// </summary>
internal static class SignalsResizeTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("resize_settled: ignore-terminal-dimensions", IgnoreTerminalDimensions);
        yield return new TestCase("resize_settled: unchanged dimensions", UnchangedDimensions);
        yield return new TestCase("resize_settled: unchanged layout", UnchangedLayout);
        yield return new TestCase("resize_settled: actual rebuild", ActualRebuild);
        yield return new TestCase("SIGINT double-register throws", SigintDoubleRegister);
    }

    private static void SigintDoubleRegister()
    {
        Signals.InstallSigintHandler();
        Harness.AssertThrows<EngineInvariantException>("second SIGINT", Signals.InstallSigintHandler);
    }

    private static void IgnoreTerminalDimensions()
    {
        WithDims("80", "24", () =>
        {
            var config = new TerminalConfig
            {
                IgnoreTerminalDimensions = true,
                CanvasWidth = 20,
                CanvasHeight = 8,
            };
            Terminal terminal = Terminal.New("hello world", config);
            Environment.SetEnvironmentVariable("COLUMNS", "10");
            Environment.SetEnvironmentVariable("LINES", "5");
            StartQuietWindow(terminal);
            Harness.AssertTrue("suppressed by ignore-terminal-dimensions", !terminal.ResizeSettled());
        });
    }

    private static void UnchangedDimensions()
    {
        WithDims("80", "24", () =>
        {
            var config = new TerminalConfig
            {
                IgnoreTerminalDimensions = false,
                CanvasWidth = 0,
            };
            Terminal terminal = Terminal.New("hello world", config);
            StartQuietWindow(terminal);
            Harness.AssertTrue("suppressed by unchanged dimensions", !terminal.ResizeSettled());
        });
    }

    private static void UnchangedLayout()
    {
        WithDims("80", "24", () =>
        {
            var config = new TerminalConfig { IgnoreTerminalDimensions = false };
            Terminal terminal = Terminal.New("hi", config);
            Environment.SetEnvironmentVariable("COLUMNS", "100");
            Environment.SetEnvironmentVariable("LINES", "30");
            Layout same = Terminal.ComputeLayout(config, terminal.InputLineLengths, 100, 30);
            Harness.AssertEqual("layout unchanged vs stored", terminal.Layout, same);
            StartQuietWindow(terminal);
            Harness.AssertTrue("suppressed by unchanged layout", !terminal.ResizeSettled());
        });
    }

    private static void ActualRebuild()
    {
        WithDims("80", "24", () =>
        {
            var config = new TerminalConfig
            {
                IgnoreTerminalDimensions = false,
                CanvasWidth = 0,
            };
            Terminal terminal = Terminal.New("hello world", config);
            Environment.SetEnvironmentVariable("COLUMNS", "40");
            Environment.SetEnvironmentVariable("LINES", "24");
            Layout changed = Terminal.ComputeLayout(config, terminal.InputLineLengths, 40, 24);
            Harness.AssertTrue("layout actually differs", changed != terminal.Layout);
            StartQuietWindow(terminal);
            Harness.AssertTrue("rebuild", terminal.ResizeSettled());
        });
    }

    /// <summary>
    /// Consume the SIGWINCH flag and start the 50 ms quiet window, then wait
    /// for it to expire. A single post-sleep call would re-stamp "now" and
    /// fail the quiet check.
    /// </summary>
    private static void StartQuietWindow(Terminal terminal)
    {
        Signals.ForceResized();
        Harness.AssertTrue("quiet window not yet expired", !terminal.ResizeSettled());
        Thread.Sleep(60);
    }

    private static void WithDims(string columns, string lines, Action action)
    {
        string? oldColumns = Environment.GetEnvironmentVariable("COLUMNS");
        string? oldLines = Environment.GetEnvironmentVariable("LINES");
        try
        {
            Environment.SetEnvironmentVariable("COLUMNS", columns);
            Environment.SetEnvironmentVariable("LINES", lines);
            Signals.ClearFlags();
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLUMNS", oldColumns);
            Environment.SetEnvironmentVariable("LINES", oldLines);
            Signals.ClearFlags();
        }
    }
}
