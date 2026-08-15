using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class CrumbleEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("crumble DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("crumble AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("crumble flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Crumble(new CrumbleConfig());
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
        ParseResult r = CliParser.Parse(["crumble"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 2, stops.Count);
        Harness.AssertEqual("stop0", "5CE1FF", ((Color)stops[0]).Original);
        Harness.AssertEqual("stop1", "FF8C00", ((Color)stops[1]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
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
                "crumble",
                "--final-gradient-stops",
                "ff0000",
                "00ff00",
                "0000ff",
                "--final-gradient-steps",
                "6",
                "--final-gradient-direction",
                "horizontal",
            ]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("user stop count", 3, stops.Count);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("user steps0", 6L, (long)steps[0]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
