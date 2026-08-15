using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class BlackholeEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("blackhole DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("blackhole AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("blackhole flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Blackhole(new BlackholeConfig());
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
        ParseResult r = CliParser.Parse(["blackhole"]);
        Harness.AssertEqual("blackhole color", "ffffff", ((Color)r.EffectOptions["--blackhole-color"]).Original);
        var starColors = (List<object>)r.EffectOptions["--star-colors"];
        Harness.AssertEqual("star count", 6, starColors.Count);
        Harness.AssertEqual("star0", "ffcc0d", ((Color)starColors[0]).Original);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "8A008A", ((Color)stops[0]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
        Harness.AssertEqual("steps0", 9L, (long)steps[0]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Diagonal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "blackhole",
                "--blackhole-color",
                "00ffff",
                "--star-colors",
                "ff0000",
                "00ff00",
                "--final-gradient-steps",
                "6",
                "--final-gradient-direction",
                "horizontal",
            ]);
        Harness.AssertEqual("user color", "00ffff", ((Color)r.EffectOptions["--blackhole-color"]).Original);
        var starColors = (List<object>)r.EffectOptions["--star-colors"];
        Harness.AssertEqual("user star count", 2, starColors.Count);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("user steps0", 6L, (long)steps[0]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
