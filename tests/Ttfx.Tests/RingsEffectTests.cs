using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class RingsEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("rings DispatchCallback hides character", DispatchCallback);
        yield return new TestCase("rings defaults", DefaultOptions);
    }

    private static void DispatchCallback()
    {
        var effect = new Rings(new RingsConfig());
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
        CharId id = new CharId(0);
        world.Terminal.SetCharacterVisibility(id, true);
        effect.DispatchCallback(world, id, new EffectCallback(0, []));
        Harness.AssertTrue("visibility cleared", !world.Terminal.Arena[(int)id.Value].IsVisible);
    }

    private static void DefaultOptions()
    {
        ParseResult r = CliParser.Parse(["rings"]);
        var colors = (List<object>)r.EffectOptions["--ring-colors"];
        Harness.AssertEqual("ring color count", 3, colors.Count);
        Harness.AssertEqual("ring gap", 0.1, (double)r.EffectOptions["--ring-gap"]);
        Harness.AssertEqual("spin duration", 200L, (long)r.EffectOptions["--spin-duration"]);
        (double lower, double upper) = ((double, double))r.EffectOptions["--spin-speed"];
        Harness.AssertEqual("spin speed lower", 0.25, lower);
        Harness.AssertEqual("spin speed upper", 1.0, upper);
        Harness.AssertEqual("disperse duration", 200L, (long)r.EffectOptions["--disperse-duration"]);
        Harness.AssertEqual("cycles", 3L, (long)r.EffectOptions["--spin-disperse-cycles"]);
    }
}
