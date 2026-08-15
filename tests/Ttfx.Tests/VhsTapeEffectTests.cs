using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class VhsTapeEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("vhstape DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("vhstape AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("vhstape flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new VhsTape(new VhsTapeConfig());
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
        ParseResult r = CliParser.Parse(["vhstape"]);
        Harness.AssertEqual("total glitch time", 600L, r.EffectOptions["--total-glitch-time"]);
        Harness.AssertEqual("glitch line chance", 0.05, r.EffectOptions["--glitch-line-chance"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(["vhstape", "--total-glitch-time", "150", "--noise-chance", "0.01"]);
        Harness.AssertEqual("user glitch time", 150L, r.EffectOptions["--total-glitch-time"]);
        Harness.AssertEqual("user noise chance", 0.01, r.EffectOptions["--noise-chance"]);
    }
}
