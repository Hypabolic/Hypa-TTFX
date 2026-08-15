using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class SprayEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("spray DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("spray AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("spray flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Spray(new SprayConfig());
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
        ParseResult r = CliParser.Parse(["spray"]);
        Harness.AssertEqual("position", SprayPosition.E, (SprayPosition)r.EffectOptions["--spray-position"]);
        Harness.AssertEqual("volume", 0.005, (double)r.EffectOptions["--spray-volume"]);
        (double min, double max) = ((double, double))r.EffectOptions["--movement-speed-range"];
        Harness.AssertEqual("range min", 0.6, min);
        Harness.AssertEqual("range max", 1.4, max);
        Harness.AssertEqual("ease", Easing.OutExpo, (Easing)r.EffectOptions["--movement-easing"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
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
                "spray",
                "--spray-position",
                "nw",
                "--spray-volume",
                "0.02",
                "--movement-speed-range",
                "0.3-0.8",
                "--movement-easing",
                "in_out_quad",
            ]);
        Harness.AssertEqual("user position", SprayPosition.Nw, (SprayPosition)r.EffectOptions["--spray-position"]);
        Harness.AssertEqual("user volume", 0.02, (double)r.EffectOptions["--spray-volume"]);
        (double min, double max) = ((double, double))r.EffectOptions["--movement-speed-range"];
        Harness.AssertEqual("user range min", 0.3, min);
        Harness.AssertEqual("user range max", 0.8, max);
        Harness.AssertEqual("user ease", Easing.InOutQuad, (Easing)r.EffectOptions["--movement-easing"]);
    }
}
