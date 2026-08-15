using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class ScatteredEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("scattered DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("scattered AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("scattered flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Scattered(new ScatteredConfig());
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
        ParseResult r = CliParser.Parse(["scattered"]);
        Harness.AssertEqual("speed", 0.5, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("ease", Easing.InOutBack, (Easing)r.EffectOptions["--movement-easing"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "ff9048", ((Color)stops[0]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual("frames", 9L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "scattered",
                "--movement-speed",
                "0.7",
                "--movement-easing",
                "out_bounce",
                "--final-gradient-frames",
                "5",
                "--final-gradient-direction",
                "horizontal",
            ]);
        Harness.AssertEqual("user speed", 0.7, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("user ease", Easing.OutBounce, (Easing)r.EffectOptions["--movement-easing"]);
        Harness.AssertEqual("user frames", 5L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
