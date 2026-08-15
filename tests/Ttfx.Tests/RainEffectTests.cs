using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class RainEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("rain DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("rain AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("rain flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Rain(new RainConfig());
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
        ParseResult r = CliParser.Parse(["rain"]);
        var colors = (List<object>)r.EffectOptions["--rain-colors"];
        Harness.AssertEqual("color count", 8, colors.Count);
        (double min, double max) = ((double, double))r.EffectOptions["--movement-speed"];
        Harness.AssertEqual("speed min", 0.33, min);
        Harness.AssertEqual("speed max", 0.57, max);
        var symbols = (List<object>)r.EffectOptions["--rain-symbols"];
        Harness.AssertEqual("symbol count", 5, symbols.Count);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "488bff", ((Color)stops[0]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Diagonal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
        Harness.AssertEqual("ease", Easing.InQuart, (Easing)r.EffectOptions["--movement-easing"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "rain",
                "--rain-colors",
                "00ff00",
                "ff0000",
                "--movement-speed",
                "0.8-1.2",
                "--rain-symbols",
                ".",
                ",",
                "--final-gradient-direction",
                "vertical",
                "--movement-easing",
                "out_bounce",
            ]);
        var colors = (List<object>)r.EffectOptions["--rain-colors"];
        Harness.AssertEqual("user color count", 2, colors.Count);
        (double min, double max) = ((double, double))r.EffectOptions["--movement-speed"];
        Harness.AssertEqual("user speed min", 0.8, min);
        Harness.AssertEqual("user speed max", 1.2, max);
        var symbols = (List<object>)r.EffectOptions["--rain-symbols"];
        Harness.AssertEqual("user symbol count", 2, symbols.Count);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
        Harness.AssertEqual("user ease", Easing.OutBounce, (Easing)r.EffectOptions["--movement-easing"]);
    }
}
