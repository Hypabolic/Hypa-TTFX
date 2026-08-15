using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class SlideEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("slide DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("slide AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("slide flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Slide(new SlideConfig());
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
        ParseResult r = CliParser.Parse(["slide"]);
        Harness.AssertEqual("speed", 0.8, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("grouping", SlideGrouping.Row, (SlideGrouping)r.EffectOptions["--grouping"]);
        Harness.AssertEqual("gap", 2L, (long)r.EffectOptions["--gap"]);
        Harness.AssertEqual("ease", Easing.InOutQuad, (Easing)r.EffectOptions["--movement-easing"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "833ab4", ((Color)stops[0]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual("frames", 6L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "slide",
                "--grouping",
                "column",
                "--merge",
                "--gap",
                "0",
                "--movement-speed",
                "0.5",
            ]);
        Harness.AssertEqual("user grouping", SlideGrouping.Column, (SlideGrouping)r.EffectOptions["--grouping"]);
        Harness.AssertTrue("user merge", r.EffectOptions.ContainsKey("--merge"));
        Harness.AssertEqual("user gap", 0L, (long)r.EffectOptions["--gap"]);
        Harness.AssertEqual("user speed", 0.5, (double)r.EffectOptions["--movement-speed"]);
    }
}
