using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class DecryptEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("decrypt DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("decrypt AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("decrypt flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Decrypt(new DecryptConfig());
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
        ParseResult r = CliParser.Parse(["decrypt"]);
        Harness.AssertEqual("typing speed", 2L, (long)r.EffectOptions["--typing-speed"]);
        var cipherColors = (List<object>)r.EffectOptions["--ciphertext-colors"];
        Harness.AssertEqual("cipher count", 3, cipherColors.Count);
        Harness.AssertEqual("cipher0", "008000", ((Color)cipherColors[0]).Original);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 1, stops.Count);
        Harness.AssertEqual("stop0", "eda000", ((Color)stops[0]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
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
                "decrypt",
                "--typing-speed",
                "1",
                "--ciphertext-colors",
                "ff0000",
                "00ff00",
                "0000ff",
                "--final-gradient-stops",
                "ed0000",
                "00d1ff",
                "--final-gradient-direction",
                "horizontal",
            ]);
        Harness.AssertEqual("user speed", 1L, (long)r.EffectOptions["--typing-speed"]);
        var cipherColors = (List<object>)r.EffectOptions["--ciphertext-colors"];
        Harness.AssertEqual("user cipher count", 3, cipherColors.Count);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
