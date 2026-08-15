using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class BeamsEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("beams DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("beams AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("beams flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Beams(new BeamsConfig());
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
        ParseResult r = CliParser.Parse(["beams"]);
        var rowSymbols = (List<object>)r.EffectOptions["--beam-row-symbols"];
        Harness.AssertEqual("row symbol count", 3, rowSymbols.Count);
        Harness.AssertEqual("row0", "▂", (string)rowSymbols[0]);
        Harness.AssertEqual("row1", "▁", (string)rowSymbols[1]);
        Harness.AssertEqual("row2", "_", (string)rowSymbols[2]);
        var colSymbols = (List<object>)r.EffectOptions["--beam-column-symbols"];
        Harness.AssertEqual("col symbol count", 4, colSymbols.Count);
        Harness.AssertEqual("col0", "▌", (string)colSymbols[0]);
        Harness.AssertEqual("delay", 6L, (long)r.EffectOptions["--beam-delay"]);
        (long rowMin, long rowMax) = ((long, long))r.EffectOptions["--beam-row-speed-range"];
        Harness.AssertEqual("row speed min", 15L, rowMin);
        Harness.AssertEqual("row speed max", 60L, rowMax);
        (long colMin, long colMax) = ((long, long))r.EffectOptions["--beam-column-speed-range"];
        Harness.AssertEqual("col speed min", 9L, colMin);
        Harness.AssertEqual("col speed max", 15L, colMax);
        var beamStops = (List<object>)r.EffectOptions["--beam-gradient-stops"];
        Harness.AssertEqual("beam stop count", 3, beamStops.Count);
        Harness.AssertEqual("beam stop0", "ffffff", ((Color)beamStops[0]).Original);
        Harness.AssertEqual("beam stop1", "00D1FF", ((Color)beamStops[1]).Original);
        Harness.AssertEqual("beam stop2", "8A008A", ((Color)beamStops[2]).Original);
        var beamSteps = (List<object>)r.EffectOptions["--beam-gradient-steps"];
        Harness.AssertEqual("beam steps count", 2, beamSteps.Count);
        Harness.AssertEqual("beam steps0", 2L, (long)beamSteps[0]);
        Harness.AssertEqual("beam steps1", 6L, (long)beamSteps[1]);
        Harness.AssertEqual("beam frames", 2L, (long)r.EffectOptions["--beam-gradient-frames"]);
        var finalStops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("final stop count", 3, finalStops.Count);
        Harness.AssertEqual("final stop0", "8A008A", ((Color)finalStops[0]).Original);
        Harness.AssertEqual("final stop1", "00D1FF", ((Color)finalStops[1]).Original);
        Harness.AssertEqual("final stop2", "ffffff", ((Color)finalStops[2]).Original);
        var finalSteps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("final steps count", 1, finalSteps.Count);
        Harness.AssertEqual("final steps0", 12L, (long)finalSteps[0]);
        Harness.AssertEqual("final frames", 4L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual(
            "final grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
        Harness.AssertEqual("wipe speed", 3L, (long)r.EffectOptions["--final-wipe-speed"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "beams",
                "--beam-row-symbols",
                "-",
                "=",
                "--beam-column-symbols",
                ".",
                ":",
                "--beam-delay",
                "2",
                "--beam-row-speed-range",
                "30-80",
                "--beam-column-speed-range",
                "12-20",
                "--beam-gradient-frames",
                "1",
                "--final-gradient-direction",
                "horizontal",
                "--final-wipe-speed",
                "5",
            ]);
        var rowSymbols = (List<object>)r.EffectOptions["--beam-row-symbols"];
        Harness.AssertEqual("user row count", 2, rowSymbols.Count);
        Harness.AssertEqual("user row0", "-", (string)rowSymbols[0]);
        Harness.AssertEqual("user delay", 2L, (long)r.EffectOptions["--beam-delay"]);
        (long rowMin, long rowMax) = ((long, long))r.EffectOptions["--beam-row-speed-range"];
        Harness.AssertEqual("user row min", 30L, rowMin);
        Harness.AssertEqual("user row max", 80L, rowMax);
        Harness.AssertEqual("user beam frames", 1L, (long)r.EffectOptions["--beam-gradient-frames"]);
        Harness.AssertEqual(
            "user final dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
        Harness.AssertEqual("user wipe speed", 5L, (long)r.EffectOptions["--final-wipe-speed"]);
    }
}
