using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class ExpandEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("expand DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("expand AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("expand flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Expand(new ExpandConfig());
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
        ParseResult r = CliParser.Parse(["expand"]);
        Harness.AssertEqual("ease", Easing.InOutQuart, (Easing)r.EffectOptions["--expand-easing"]);
        Harness.AssertEqual("speed", 0.35, (double)r.EffectOptions["--movement-speed"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "8A008A", ((Color)stops[0]).Original);
        Harness.AssertEqual("stop1", "00D1FF", ((Color)stops[1]).Original);
        Harness.AssertEqual("stop2", "FFFFFF", ((Color)stops[2]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "expand",
                "--expand-easing",
                "out_bounce",
                "--movement-speed",
                "0.8",
                "--final-gradient-stops",
                "ff0000",
                "--final-gradient-steps",
                "6",
                "--final-gradient-direction",
                "horizontal",
            ]);
        Harness.AssertEqual("user ease", Easing.OutBounce, (Easing)r.EffectOptions["--expand-easing"]);
        Harness.AssertEqual("user speed", 0.8, (double)r.EffectOptions["--movement-speed"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("user stop count", 1, stops.Count);
        Harness.AssertEqual("user stop", "ff0000", ((Color)stops[0]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("user steps count", 1, steps.Count);
        Harness.AssertEqual("user steps", 6L, (long)steps[0]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
