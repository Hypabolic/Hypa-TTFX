using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class FireworksEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("fireworks DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("fireworks AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("fireworks flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Fireworks(new FireworksConfig());
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
        ParseResult r = CliParser.Parse(["fireworks"]);
        Harness.AssertTrue("explode-anywhere absent", !r.EffectOptions.ContainsKey("--explode-anywhere"));
        var colors = (List<object>)r.EffectOptions["--firework-colors"];
        Harness.AssertEqual("firework color count", 5, colors.Count);
        Harness.AssertEqual("firework symbol", "o", (string)r.EffectOptions["--firework-symbol"]);
        Harness.AssertEqual("firework volume", 0.05, (double)r.EffectOptions["--firework-volume"]);
        Harness.AssertEqual("launch delay", 45L, (long)r.EffectOptions["--launch-delay"]);
        Harness.AssertEqual("explode distance", 0.2, (double)r.EffectOptions["--explode-distance"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "fireworks",
                "--explode-anywhere",
                "--firework-colors",
                "ff0000",
                "00ff00",
                "--firework-symbol",
                "*",
                "--firework-volume",
                "0.12",
                "--launch-delay",
                "10",
                "--explode-distance",
                "0.4",
                "--final-gradient-direction",
                "diagonal",
            ]);
        Harness.AssertTrue("explode-anywhere set", r.EffectOptions.ContainsKey("--explode-anywhere"));
        var colors = (List<object>)r.EffectOptions["--firework-colors"];
        Harness.AssertEqual("user color count", 2, colors.Count);
        Harness.AssertEqual("user symbol", "*", (string)r.EffectOptions["--firework-symbol"]);
        Harness.AssertEqual("user volume", 0.12, (double)r.EffectOptions["--firework-volume"]);
        Harness.AssertEqual("user launch delay", 10L, (long)r.EffectOptions["--launch-delay"]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Diagonal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
