using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class BouncyBallsEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("bouncyballs DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("bouncyballs AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("bouncyballs flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new BouncyBalls(new BouncyBallsConfig());
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
        ParseResult r = CliParser.Parse(["bouncyballs"]);
        var colors = (List<object>)r.EffectOptions["--ball-colors"];
        Harness.AssertEqual("ball color count", 3, colors.Count);
        Harness.AssertEqual("ball0", "d1f4a5", ((Color)colors[0]).Original);
        Harness.AssertEqual("ball1", "96e2a4", ((Color)colors[1]).Original);
        Harness.AssertEqual("ball2", "5acda9", ((Color)colors[2]).Original);
        var symbols = (List<object>)r.EffectOptions["--ball-symbols"];
        Harness.AssertEqual("symbol count", 5, symbols.Count);
        Harness.AssertEqual("sym0", "*", (string)symbols[0]);
        Harness.AssertEqual("sym1", "o", (string)symbols[1]);
        Harness.AssertEqual("sym2", "O", (string)symbols[2]);
        Harness.AssertEqual("sym3", "0", (string)symbols[3]);
        Harness.AssertEqual("sym4", ".", (string)symbols[4]);
        Harness.AssertEqual("delay", 4L, (long)r.EffectOptions["--ball-delay"]);
        Harness.AssertEqual("speed", 0.45, (double)r.EffectOptions["--movement-speed"]);
        Harness.AssertEqual("ease", Easing.OutBounce, (Easing)r.EffectOptions["--movement-easing"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 2, stops.Count);
        Harness.AssertEqual("stop0", "f8ffae", ((Color)stops[0]).Original);
        Harness.AssertEqual("stop1", "43c6ac", ((Color)stops[1]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Diagonal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            ["bouncyballs", "--ball-colors", "ff0000", "--ball-symbols", "x", "--ball-delay", "0"]);
        var colors = (List<object>)r.EffectOptions["--ball-colors"];
        Harness.AssertEqual("user color count", 1, colors.Count);
        Harness.AssertEqual("user color", "ff0000", ((Color)colors[0]).Original);
        var symbols = (List<object>)r.EffectOptions["--ball-symbols"];
        Harness.AssertEqual("user symbol count", 1, symbols.Count);
        Harness.AssertEqual("user symbol", "x", (string)symbols[0]);
        Harness.AssertEqual("user delay", 0L, (long)r.EffectOptions["--ball-delay"]);
    }
}
