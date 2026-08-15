using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class OrbittingVolleyEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("orbittingvolley DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("orbittingvolley defaults", DefaultOptions);
    }

    private static void DispatchCallback()
    {
        var effect = new OrbittingVolley(new OrbittingVolleyConfig());
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
        ParseResult r = CliParser.Parse(["orbittingvolley"]);
        Harness.AssertEqual("top symbol", "█", (string)r.EffectOptions["--top-launcher-symbol"]);
        Harness.AssertEqual("launcher speed", 0.8, (double)r.EffectOptions["--launcher-movement-speed"]);
        Harness.AssertEqual("character speed", 1.5, (double)r.EffectOptions["--character-movement-speed"]);
        Harness.AssertEqual("volley size", 0.03, (double)r.EffectOptions["--volley-size"]);
        Harness.AssertEqual("launch delay", 30L, (long)r.EffectOptions["--launch-delay"]);
        Harness.AssertEqual(
            "character easing",
            Easing.OutSine,
            (Easing)r.EffectOptions["--character-easing"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Radial,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
