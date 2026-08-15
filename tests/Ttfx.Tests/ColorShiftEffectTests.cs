using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class ColorShiftEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("colorshift DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("colorshift AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("colorshift flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        ParseResult r = CliParser.Parse(["colorshift"]);
        var effect = ColorShift.FromOptions(r.EffectOptions);
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
        effect.Build(world);
        effect.DispatchCallback(world, new CharId(0), new EffectCallback(0, []));
        Harness.AssertTrue("dispatch runs", true);
    }

    private static void DefaultOptions()
    {
        ParseResult r = CliParser.Parse(["colorshift"]);
        var stops = (List<object>)r.EffectOptions["--gradient-stops"];
        Harness.AssertEqual("stop count", 7, stops.Count);
        Harness.AssertEqual("stop0", "e81416", ((Color)stops[0]).Original);
        var steps = (List<object>)r.EffectOptions["--gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual("frames", 2L, (long)r.EffectOptions["--gradient-frames"]);
        Harness.AssertTrue("no-travel absent", !r.EffectOptions.ContainsKey("--no-travel"));
        Harness.AssertEqual(
            "travel dir",
            GradientDirection.Radial,
            (GradientDirection)r.EffectOptions["--travel-direction"]);
        Harness.AssertTrue("reverse absent", !r.EffectOptions.ContainsKey("--reverse-travel-direction"));
        Harness.AssertTrue("no-loop absent", !r.EffectOptions.ContainsKey("--no-loop"));
        Harness.AssertEqual("cycles", 3L, (long)r.EffectOptions["--cycles"]);
        Harness.AssertTrue("skip-final absent", !r.EffectOptions.ContainsKey("--skip-final-gradient"));
        Harness.AssertEqual(
            "final grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "colorshift",
                "--gradient-stops",
                "ff0000",
                "00ff00",
                "--gradient-steps",
                "6",
                "--gradient-frames",
                "3",
                "--no-loop",
                "--travel-direction",
                "horizontal",
                "--reverse-travel-direction",
                "--cycles",
                "1",
                "--skip-final-gradient",
                "--final-gradient-direction",
                "diagonal",
            ]);
        var stops = (List<object>)r.EffectOptions["--gradient-stops"];
        Harness.AssertEqual("user stop count", 2, stops.Count);
        Harness.AssertEqual("user frames", 3L, (long)r.EffectOptions["--gradient-frames"]);
        Harness.AssertTrue("no-loop present", r.EffectOptions.ContainsKey("--no-loop"));
        Harness.AssertEqual(
            "user travel",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--travel-direction"]);
        Harness.AssertTrue("reverse present", r.EffectOptions.ContainsKey("--reverse-travel-direction"));
        Harness.AssertEqual("user cycles", 1L, (long)r.EffectOptions["--cycles"]);
        Harness.AssertTrue("skip present", r.EffectOptions.ContainsKey("--skip-final-gradient"));
        Harness.AssertEqual(
            "user final dir",
            GradientDirection.Diagonal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
