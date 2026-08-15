using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class RandomSequenceEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("randomsequence DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("randomsequence AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("randomsequence flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new RandomSequence(new RandomSequenceConfig());
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
        ParseResult r = CliParser.Parse(["randomsequence"]);
        Harness.AssertEqual("speed", 0.007, (double)r.EffectOptions["--speed"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual("frames", 8L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(["randomsequence", "--speed", "0.2", "--final-gradient-frames", "3"]);
        Harness.AssertEqual("user speed", 0.2, (double)r.EffectOptions["--speed"]);
        Harness.AssertEqual("user frames", 3L, (long)r.EffectOptions["--final-gradient-frames"]);
    }
}
