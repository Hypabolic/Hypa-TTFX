using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class SynthGridEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("synthgrid DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("synthgrid AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("synthgrid flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new SynthGrid(new SynthGridConfig());
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
        effect.DispatchCallback(world, new CharId(0), new EffectCallback(999, []));
        Harness.AssertTrue("dispatch is callable", true);
    }

    private static void DefaultOptions()
    {
        ParseResult r = CliParser.Parse(["synthgrid"]);
        Harness.AssertEqual("max active blocks", 0.1, (double)r.EffectOptions["--max-active-blocks"]);
        Harness.AssertEqual("grid row symbol", "─", (string)r.EffectOptions["--grid-row-symbol"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(["synthgrid", "--max-active-blocks", "0.5", "--grid-row-symbol", "="]);
        Harness.AssertEqual("user max active blocks", 0.5, (double)r.EffectOptions["--max-active-blocks"]);
        Harness.AssertEqual("user grid row symbol", "=", (string)r.EffectOptions["--grid-row-symbol"]);
    }
}
