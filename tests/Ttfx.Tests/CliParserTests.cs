using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class CliParserTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("defaults", Defaults);
        yield return new TestCase("beam-row-symbols lone dash", BeamRowSymbolsLoneDash);
        yield return new TestCase("beam-column-symbols option-looking", BeamColumnSymbolsDots);
        yield return new TestCase("double-dash terminator value", DoubleDashTerminator);
        yield return new TestCase("canvas-width negative", CanvasWidthNegative);
        yield return new TestCase("frame-rate negative unexpected", FrameRateNegativeUnexpected);
        yield return new TestCase("tab-width whitespace rejected", TabWidthWhitespace);
        yield return new TestCase("root before subcommand", RootBeforeSubcommand);
        yield return new TestCase("root after subcommand", RootAfterSubcommand);
        yield return new TestCase("probe is root flag", ProbeIsRootFlag);
        yield return new TestCase("probe not in registry", ProbeNotInRegistry);
        yield return new TestCase("unknown effect", UnknownEffect);
        yield return new TestCase("unknown option", UnknownOption);
        yield return new TestCase("include exclude conflict", IncludeExcludeConflict);
        yield return new TestCase("bad easing", BadEasing);
        yield return new TestCase("easing case fold", EasingCaseFold);
        yield return new TestCase("anchor case sensitive", AnchorCaseSensitive);
        yield return new TestCase("equals attached value", EqualsAttachedValue);
        yield return new TestCase("print-completion shells", PrintCompletionShells);
        yield return new TestCase("seed ulong", SeedUlong);
        yield return new TestCase("all fifteen terminal options", FifteenTerminalOptions);
    }

    private static void Defaults()
    {
        ParseResult r = CliParser.Parse([]);
        Harness.AssertEqual("tab-width default", 4L, r.Root.TabWidth);
        Harness.AssertEqual("frame-rate default", 60L, r.Root.FrameRate);
        Harness.AssertEqual("canvas-width default", -1L, r.Root.CanvasWidth);
        Harness.AssertEqual("canvas-height default", -1L, r.Root.CanvasHeight);
        Harness.AssertEqual("anchor-canvas default", Anchor.Sw, r.Root.AnchorCanvas);
        Harness.AssertEqual("anchor-text default", Anchor.Sw, r.Root.AnchorText);
        Harness.AssertEqual("existing-color default", ExistingColorHandling.Ignore, r.Root.ExistingColorHandling);
        Harness.AssertEqual("bg color default", "#000000", r.Root.TerminalBackgroundColor.Original);
        Harness.AssertTrue("xterm-colors default", !r.Root.XtermColors);
        Harness.AssertTrue("no-color default", !r.Root.NoColor);
        Harness.AssertTrue("wrap-text default", !r.Root.WrapText);
        Harness.AssertTrue("ignore-term default", !r.Root.IgnoreTerminalDimensions);
        Harness.AssertTrue("reuse-canvas default", !r.Root.ReuseCanvas);
        Harness.AssertTrue("no-eol default", !r.Root.NoEol);
        Harness.AssertTrue("no-restore default", !r.Root.NoRestoreCursor);
        Harness.AssertTrue("seed default", r.Root.Seed is null);
        Harness.AssertTrue("no effect", r.EffectName is null);
    }

    private static void BeamRowSymbolsLoneDash()
    {
        ParseResult r = CliParser.Parse(["beams", "--beam-row-symbols", "-", "="]);
        Harness.AssertEqual("effect", "beams", r.EffectName);
        var symbols = (List<object>)r.EffectOptions["--beam-row-symbols"];
        Harness.AssertEqual("count", 2, symbols.Count);
        Harness.AssertEqual("dash", "-", (string)symbols[0]);
        Harness.AssertEqual("equals", "=", (string)symbols[1]);
    }

    private static void BeamColumnSymbolsDots()
    {
        ParseResult r = CliParser.Parse(["beams", "--beam-column-symbols", ".", ":", "="]);
        var symbols = (List<object>)r.EffectOptions["--beam-column-symbols"];
        Harness.AssertEqual("count", 3, symbols.Count);
        Harness.AssertEqual("dot", ".", (string)symbols[0]);
        Harness.AssertEqual("colon", ":", (string)symbols[1]);
        Harness.AssertEqual("equals", "=", (string)symbols[2]);
    }

    private static void DoubleDashTerminator()
    {
        ParseResult r = CliParser.Parse(["--frame-rate", "--", "0", "wipe"]);
        Harness.AssertEqual("frame-rate via --", 0L, r.Root.FrameRate);
        Harness.AssertEqual("effect", "wipe", r.EffectName);
        Harness.AssertThrows<UsageError>("frame-rate -- -1", () => CliParser.Parse(["--frame-rate", "--", "-1", "wipe"]));
    }

    private static void CanvasWidthNegative()
    {
        ParseResult r = CliParser.Parse(["--canvas-width", "-1", "wipe"]);
        Harness.AssertEqual("canvas-width", -1L, r.Root.CanvasWidth);
        Harness.AssertThrows<UsageError>("canvas-width -2", () => CliParser.Parse(["--canvas-width", "-2"]));
    }

    private static void FrameRateNegativeUnexpected()
    {
        Harness.AssertThrows<UsageError>("frame-rate -1", () => CliParser.Parse(["--frame-rate", "-1"]));
    }

    private static void TabWidthWhitespace()
    {
        Harness.AssertThrows<UsageError>("tab-width padded", () => CliParser.Parse(["--tab-width", " 4 "]));
        Harness.AssertThrows<UsageError>("tab-width 0", () => CliParser.Parse(["--tab-width", "0"]));
        ParseResult r = CliParser.Parse(["--tab-width", "+4"]);
        Harness.AssertEqual("tab-width +4", 4L, r.Root.TabWidth);
    }

    private static void RootBeforeSubcommand()
    {
        ParseResult r = CliParser.Parse(["--no-color", "wipe"]);
        Harness.AssertTrue("no-color", r.Root.NoColor);
        Harness.AssertEqual("effect", "wipe", r.EffectName);
    }

    private static void RootAfterSubcommand()
    {
        Harness.AssertThrows<UsageError>("wipe --no-color", () => CliParser.Parse(["wipe", "--no-color"]));
    }

    private static void ProbeIsRootFlag()
    {
        ParseResult r = CliParser.Parse(["--probe"]);
        Harness.AssertTrue("probe set", r.Root.Probe);
        Harness.AssertTrue("no effect", r.EffectName is null);
    }

    private static void ProbeNotInRegistry()
    {
        Harness.AssertTrue("registry has no probe", !EffectRegistry.Contains("probe"));
        foreach (EffectSpec spec in EffectRegistry.Effects)
        {
            Harness.AssertTrue("name is not probe", spec.Name != "probe");
        }
    }

    private static void UnknownEffect()
    {
        Harness.AssertThrows<UsageError>("nosucheffect", () => CliParser.Parse(["nosucheffect"]));
    }

    private static void UnknownOption()
    {
        Harness.AssertThrows<UsageError>("--no-such-option", () => CliParser.Parse(["--no-such-option", "wipe"]));
    }

    private static void IncludeExcludeConflict()
    {
        Harness.AssertThrows<UsageError>(
            "include+exclude",
            () => CliParser.Parse(["-R", "--include-effects", "a", "--exclude-effects", "b"]));
    }

    private static void BadEasing()
    {
        Harness.AssertThrows<UsageError>(
            "not_an_ease",
            () => CliParser.Parse(["wipe", "--wipe-ease", "not_an_ease"]));
    }

    private static void EasingCaseFold()
    {
        ParseResult r = CliParser.Parse(["wipe", "--wipe-ease", "IN_SINE"]);
        Harness.AssertEqual("ease", Easing.InSine, (Easing)r.EffectOptions["--wipe-ease"]);
    }

    private static void AnchorCaseSensitive()
    {
        Harness.AssertThrows<UsageError>("SW", () => CliParser.Parse(["--anchor-canvas", "SW"]));
        ParseResult r = CliParser.Parse(["--anchor-canvas", "c"]);
        Harness.AssertEqual("c", Anchor.C, r.Root.AnchorCanvas);
    }

    private static void EqualsAttachedValue()
    {
        ParseResult r = CliParser.Parse(["--tab-width=8", "--canvas-width=-1"]);
        Harness.AssertEqual("tab", 8L, r.Root.TabWidth);
        Harness.AssertEqual("cw", -1L, r.Root.CanvasWidth);
        Harness.AssertThrows<UsageError>("frame-rate=-1", () => CliParser.Parse(["--frame-rate=-1"]));
    }

    private static void PrintCompletionShells()
    {
        ParseResult bash = CliParser.Parse(["--print-completion", "bash"]);
        Harness.AssertEqual("bash", "bash", bash.Root.PrintCompletion);
        ParseResult zsh = CliParser.Parse(["--print-completion", "zsh"]);
        Harness.AssertEqual("zsh", "zsh", zsh.Root.PrintCompletion);
        Harness.AssertThrows<UsageError>("fish", () => CliParser.Parse(["--print-completion", "fish"]));
    }

    private static void SeedUlong()
    {
        ParseResult r = CliParser.Parse(["--seed", "18446744073709551615"]);
        Harness.AssertEqual("u64 max", ulong.MaxValue, r.Root.Seed);
        Harness.AssertThrows<UsageError>("seed overflow", () => CliParser.Parse(["--seed", "18446744073709551616"]));
        Harness.AssertThrows<UsageError>("seed -1", () => CliParser.Parse(["--seed", "-1"]));
    }

    private static void FifteenTerminalOptions()
    {
        ParseResult r = CliParser.Parse([
            "--tab-width", "3",
            "--xterm-colors",
            "--no-color",
            "--terminal-background-color", "255",
            "--existing-color-handling", "always",
            "--wrap-text",
            "--frame-rate", "0",
            "--canvas-width", "80",
            "--canvas-height", "24",
            "--anchor-canvas", "ne",
            "--anchor-text", "nw",
            "--ignore-terminal-dimensions",
            "--reuse-canvas",
            "--no-eol",
            "--no-restore-cursor",
            "wipe",
        ]);
        Harness.AssertEqual("tab", 3L, r.Root.TabWidth);
        Harness.AssertTrue("xterm", r.Root.XtermColors);
        Harness.AssertTrue("nocolor", r.Root.NoColor);
        Harness.AssertEqual("bg", "255", r.Root.TerminalBackgroundColor.Original);
        Harness.AssertEqual("ech", ExistingColorHandling.Always, r.Root.ExistingColorHandling);
        Harness.AssertTrue("wrap", r.Root.WrapText);
        Harness.AssertEqual("fps", 0L, r.Root.FrameRate);
        Harness.AssertEqual("cw", 80L, r.Root.CanvasWidth);
        Harness.AssertEqual("ch", 24L, r.Root.CanvasHeight);
        Harness.AssertEqual("ac", Anchor.Ne, r.Root.AnchorCanvas);
        Harness.AssertEqual("at", Anchor.Nw, r.Root.AnchorText);
        Harness.AssertTrue("ignore", r.Root.IgnoreTerminalDimensions);
        Harness.AssertTrue("reuse", r.Root.ReuseCanvas);
        Harness.AssertTrue("noeol", r.Root.NoEol);
        Harness.AssertTrue("norestore", r.Root.NoRestoreCursor);
    }
}
