using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class SwarmEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("swarm DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("swarm defaults", DefaultOptions);
    }

    private static void DispatchCallback()
    {
        var effect = new Swarm(new SwarmConfig());
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
        ParseResult r = CliParser.Parse(["swarm"]);
        var baseColors = (List<object>)r.EffectOptions["--base-color"];
        Harness.AssertEqual("base color count", 1, baseColors.Count);
        Harness.AssertEqual("swarm size", 0.1, (double)r.EffectOptions["--swarm-size"]);
        Harness.AssertEqual("coordination", 0.80, (double)r.EffectOptions["--swarm-coordination"]);
        (long lower, long upper) = ((long, long))r.EffectOptions["--swarm-area-count-range"];
        Harness.AssertEqual("area lower", 2L, lower);
        Harness.AssertEqual("area upper", 4L, upper);
    }
}
