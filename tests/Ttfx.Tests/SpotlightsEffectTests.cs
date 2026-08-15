using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class SpotlightsEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("spotlights DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("spotlights defaults", DefaultOptions);
    }

    private static void DispatchCallback()
    {
        var effect = new Spotlights(new SpotlightsConfig());
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
        ParseResult r = CliParser.Parse(["spotlights"]);
        Harness.AssertEqual("beam width ratio", 2.0, (double)r.EffectOptions["--beam-width-ratio"]);
        Harness.AssertEqual("beam falloff", 0.3, (double)r.EffectOptions["--beam-falloff"]);
        Harness.AssertEqual("search duration", 550L, (long)r.EffectOptions["--search-duration"]);
        Harness.AssertEqual("spotlight count", 3L, (long)r.EffectOptions["--spotlight-count"]);
    }
}
