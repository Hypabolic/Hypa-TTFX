using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class BurnEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("burn DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("burn AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("burn flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Burn(new BurnConfig());
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
        Harness.AssertTrue("dispatch smoke path is callable", true);
    }

    private static void DefaultOptions()
    {
        ParseResult r = CliParser.Parse(["burn"]);
        Harness.AssertEqual("starting color", "837373", ((Color)r.EffectOptions["--starting-color"]).Original);
        var burnColors = (List<object>)r.EffectOptions["--burn-colors"];
        Harness.AssertEqual("burn color count", 5, burnColors.Count);
        Harness.AssertEqual("smoke chance", 0.5, (double)r.EffectOptions["--smoke-chance"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 2, stops.Count);
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
                "burn",
                "--starting-color",
                "404040",
                "--burn-colors",
                "ff0000",
                "ffa500",
                "--smoke-chance",
                "1.0",
                "--final-gradient-stops",
                "00ff00",
                "0000ff",
                "--final-gradient-steps",
                "8",
                "--final-gradient-direction",
                "horizontal",
            ]);
        Harness.AssertEqual("user starting color", "404040", ((Color)r.EffectOptions["--starting-color"]).Original);
        var burnColors = (List<object>)r.EffectOptions["--burn-colors"];
        Harness.AssertEqual("user burn color count", 2, burnColors.Count);
        Harness.AssertEqual("user smoke chance", 1.0, (double)r.EffectOptions["--smoke-chance"]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
