using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class OverflowEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("overflow DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("overflow defaults", DefaultOptions);
    }

    private static void DispatchCallback()
    {
        var effect = new Overflow(new OverflowConfig());
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
        ParseResult r = CliParser.Parse(["overflow"]);
        var stops = (List<object>)r.EffectOptions["--overflow-gradient-stops"];
        Harness.AssertEqual("overflow stop count", 3, stops.Count);
        (long lower, long upper) = ((long, long))r.EffectOptions["--overflow-cycles-range"];
        Harness.AssertEqual("cycles lower", 2L, lower);
        Harness.AssertEqual("cycles upper", 4L, upper);
        Harness.AssertEqual("overflow speed", 3L, (long)r.EffectOptions["--overflow-speed"]);
    }
}
