using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class BinaryPathEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("binarypath DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("binarypath AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("binarypath flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new BinaryPath(new BinaryPathConfig());
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
        ParseResult r = CliParser.Parse(["binarypath"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 2, stops.Count);
        Harness.AssertEqual("stop0", "00d500", ((Color)stops[0]).Original);
        Harness.AssertEqual("stop1", "007500", ((Color)stops[1]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Radial,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
        var binaryColors = (List<object>)r.EffectOptions["--binary-colors"];
        Harness.AssertEqual("binary color count", 4, binaryColors.Count);
        Harness.AssertEqual("binary0", "044E29", ((Color)binaryColors[0]).Original);
        Harness.AssertEqual("speed", 1.0, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("active groups", 0.08, (double)r.EffectOptions["--active-binary-groups"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "binarypath",
                "--binary-colors",
                "ff0000",
                "00ff00",
                "--movement-speed",
                "2",
                "--active-binary-groups",
                "0.3",
                "--final-gradient-direction",
                "vertical",
            ]);
        var binaryColors = (List<object>)r.EffectOptions["--binary-colors"];
        Harness.AssertEqual("user binary count", 2, binaryColors.Count);
        Harness.AssertEqual("user speed", 2.0, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("user active", 0.3, (double)r.EffectOptions["--active-binary-groups"]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
