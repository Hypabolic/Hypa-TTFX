using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class MiddleoutEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("middleout DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("middleout AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("middleout flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Middleout(new MiddleoutConfig());
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
        ParseResult r = CliParser.Parse(["middleout"]);
        Harness.AssertEqual("starting", "ffffff", ((Color)r.EffectOptions["--starting-color"]).Original);
        Harness.AssertEqual(
            "expand dir",
            ExpandDirection.Vertical,
            (ExpandDirection)r.EffectOptions["--expand-direction"]);
        Harness.AssertEqual("center speed", 0.6, (double)r.EffectOptions["--center-movement-speed"]);
        Harness.AssertEqual("full speed", 0.6, (double)r.EffectOptions["--full-movement-speed"]);
        Harness.AssertEqual("center ease", Easing.InOutSine, (Easing)r.EffectOptions["--center-easing"]);
        Harness.AssertEqual("full ease", Easing.InOutSine, (Easing)r.EffectOptions["--full-easing"]);
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
            [
                "middleout",
                "--expand-direction",
                "horizontal",
                "--starting-color",
                "00ff00",
                "--center-movement-speed",
                "0.3",
                "--full-movement-speed",
                "0.9",
                "--center-easing",
                "out_bounce",
                "--full-easing",
                "in_quad",
            ]);
        Harness.AssertEqual(
            "user expand dir",
            ExpandDirection.Horizontal,
            (ExpandDirection)r.EffectOptions["--expand-direction"]);
        Harness.AssertEqual("user starting", "00ff00", ((Color)r.EffectOptions["--starting-color"]).Original);
        Harness.AssertEqual("user center speed", 0.3, (double)r.EffectOptions["--center-movement-speed"]);
        Harness.AssertEqual("user full speed", 0.9, (double)r.EffectOptions["--full-movement-speed"]);
        Harness.AssertEqual("user center ease", Easing.OutBounce, (Easing)r.EffectOptions["--center-easing"]);
        Harness.AssertEqual("user full ease", Easing.InQuad, (Easing)r.EffectOptions["--full-easing"]);
    }
}
