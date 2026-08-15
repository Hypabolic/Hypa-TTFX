using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class LaserEtchEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("laseretch DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("laseretch AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("laseretch flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new LaserEtch(new LaserEtchConfig());
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
        Harness.AssertTrue("dispatch is callable", true);
    }

    private static void DefaultOptions()
    {
        ParseResult r = CliParser.Parse(["laseretch"]);
        var etchPattern = (EtchPattern)r.EffectOptions["--etch-pattern"];
        Harness.AssertEqual("etch pattern kind", EtchPatternKind.Algorithm, etchPattern.Kind);
        Harness.AssertEqual("etch speed", 1L, (long)r.EffectOptions["--etch-speed"]);
        Harness.AssertEqual("etch delay", 1L, (long)r.EffectOptions["--etch-delay"]);
        var cool = (List<object>)r.EffectOptions["--cool-gradient-stops"];
        Harness.AssertEqual("cool stop count", 2, cool.Count);
        var laser = (List<object>)r.EffectOptions["--laser-gradient-stops"];
        Harness.AssertEqual("laser stop count", 2, laser.Count);
        var spark = (List<object>)r.EffectOptions["--spark-gradient-stops"];
        Harness.AssertEqual("spark stop count", 4, spark.Count);
        Harness.AssertEqual("spark cooling frames", 7L, (long)r.EffectOptions["--spark-cooling-frames"]);
        Harness.AssertEqual("final gradient frames", 4L, (long)r.EffectOptions["--final-gradient-frames"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "laseretch",
                "--etch-pattern",
                "row_top_to_bottom",
                "--etch-speed",
                "3",
                "--etch-delay",
                "0",
                "--spark-cooling-frames",
                "3",
                "--final-gradient-direction",
                "horizontal",
            ]);
        var etchPattern = (EtchPattern)r.EffectOptions["--etch-pattern"];
        Harness.AssertEqual("group pattern", EtchPatternKind.Group, etchPattern.Kind);
        Harness.AssertEqual(
            "group value",
            CharacterGroup.RowTopToBottom,
            etchPattern.Group);
        Harness.AssertEqual("user etch speed", 3L, (long)r.EffectOptions["--etch-speed"]);
        Harness.AssertEqual("user etch delay", 0L, (long)r.EffectOptions["--etch-delay"]);
    }
}
