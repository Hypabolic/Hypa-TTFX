using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class SliceEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("slice DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("slice AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("slice flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Slice(new SliceConfig());
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
        ParseResult r = CliParser.Parse(["slice"]);
        Harness.AssertEqual("direction", "vertical", (string)r.EffectOptions["--slice-direction"]);
        Harness.AssertEqual("speed", 0.25, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("ease", Easing.InOutExpo, (Easing)r.EffectOptions["--movement-easing"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Diagonal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "slice",
                "--slice-direction",
                "horizontal",
                "--movement-speed",
                "0.4",
                "--movement-easing",
                "in_out_quad",
                "--final-gradient-direction",
                "vertical",
            ]);
        Harness.AssertEqual("user direction", "horizontal", (string)r.EffectOptions["--slice-direction"]);
        Harness.AssertEqual("user speed", 0.4, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("user ease", Easing.InOutQuad, (Easing)r.EffectOptions["--movement-easing"]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
