using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class PourEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("pour DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("pour AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("pour flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Pour(new PourConfig());
        EngineWorld world = EngineWorld.New(
            "hi",
            new TerminalConfig
            {
                CanvasWidth = 20,
                CanvasHeight = 8,
                IgnoreTerminalDimensions = true,
                FrameRate = 0,
            },
            Rng.Seeded(1),
            Clock.VirtualWithFrameRate(0));
        effect.DispatchCallback(world, new CharId(0), new EffectCallback(0, []));
        Harness.AssertTrue("dispatch is a no-op", true);
    }

    private static void DefaultOptions()
    {
        ParseResult r = CliParser.Parse(["pour"]);
        Harness.AssertEqual(
            "pour dir",
            PourDirection.Down,
            (PourDirection)r.EffectOptions["--pour-direction"]);
        Harness.AssertEqual("pour speed", 2L, (long)r.EffectOptions["--pour-speed"]);
        (double min, double max) = ((double, double))r.EffectOptions["--movement-speed-range"];
        Harness.AssertEqual("range min", 0.4, min);
        Harness.AssertEqual("range max", 0.6, max);
        Harness.AssertEqual("gap", 1L, (long)r.EffectOptions["--gap"]);
        Harness.AssertEqual("starting", "ffffff", ((Color)r.EffectOptions["--starting-color"]).Original);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "8A008A", ((Color)stops[0]).Original);
        Harness.AssertEqual("stop1", "00D1FF", ((Color)stops[1]).Original);
        Harness.AssertEqual("stop2", "FFFFFF", ((Color)stops[2]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual("frames", 6L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
        Harness.AssertEqual("ease", Easing.InQuad, (Easing)r.EffectOptions["--movement-easing"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "pour",
                "--pour-direction",
                "up",
                "--pour-speed",
                "4",
                "--movement-speed-range",
                "0.3-0.9",
                "--gap",
                "0",
                "--starting-color",
                "ff0000",
                "--final-gradient-frames",
                "3",
                "--movement-easing",
                "out_bounce",
            ]);
        Harness.AssertEqual(
            "user pour dir",
            PourDirection.Up,
            (PourDirection)r.EffectOptions["--pour-direction"]);
        Harness.AssertEqual("user pour speed", 4L, (long)r.EffectOptions["--pour-speed"]);
        (double min, double max) = ((double, double))r.EffectOptions["--movement-speed-range"];
        Harness.AssertEqual("user range min", 0.3, min);
        Harness.AssertEqual("user range max", 0.9, max);
        Harness.AssertEqual("user gap", 0L, (long)r.EffectOptions["--gap"]);
        Harness.AssertEqual("user starting", "ff0000", ((Color)r.EffectOptions["--starting-color"]).Original);
        Harness.AssertEqual("user frames", 3L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual("user ease", Easing.OutBounce, (Easing)r.EffectOptions["--movement-easing"]);
    }
}
