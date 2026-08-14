using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class ErrorCorrectEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("errorcorrect DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("errorcorrect AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("errorcorrect flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new ErrorCorrect(new ErrorCorrectConfig());
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
        ParseResult r = CliParser.Parse(["errorcorrect"]);
        Harness.AssertEqual("error pairs", 0.1, (double)r.EffectOptions["--error-pairs"]);
        Harness.AssertEqual("swap delay", 6L, (long)r.EffectOptions["--swap-delay"]);
        Harness.AssertEqual("error color", "e74c3c", ((Color)r.EffectOptions["--error-color"]).Original);
        Harness.AssertEqual("correct color", "45bf55", ((Color)r.EffectOptions["--correct-color"]).Original);
        Harness.AssertEqual("speed", 0.9, (double)r.EffectOptions["--movement-speed"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "8A008A", ((Color)stops[0]).Original);
        Harness.AssertEqual("stop1", "00D1FF", ((Color)stops[1]).Original);
        Harness.AssertEqual("stop2", "FFFFFF", ((Color)stops[2]).Original);
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
            ["errorcorrect", "--error-pairs", "0.5", "--swap-delay", "2", "--error-color", "ff8800"]);
        Harness.AssertEqual("user pairs", 0.5, (double)r.EffectOptions["--error-pairs"]);
        Harness.AssertEqual("user delay", 2L, (long)r.EffectOptions["--swap-delay"]);
        Harness.AssertEqual("user error color", "ff8800", ((Color)r.EffectOptions["--error-color"]).Original);
    }
}
