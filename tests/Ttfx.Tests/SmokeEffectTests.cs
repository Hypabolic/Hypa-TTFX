using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class SmokeEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("smoke DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("smoke AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("smoke flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Smoke(new SmokeConfig());
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
        ParseResult r = CliParser.Parse(["smoke"]);
        Harness.AssertEqual("starting color", "7A7A7A", ((Color)r.EffectOptions["--starting-color"]).Original);
        var symbols = (List<object>)r.EffectOptions["--smoke-symbols"];
        Harness.AssertEqual("symbol count", 5, symbols.Count);
        Harness.AssertTrue("whole canvas absent", !r.EffectOptions.ContainsKey("--use-whole-canvas"));
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(["smoke", "--use-whole-canvas", "--starting-color", "333333"]);
        Harness.AssertTrue("whole canvas set", r.EffectOptions.ContainsKey("--use-whole-canvas"));
        Harness.AssertEqual("user starting color", "333333", ((Color)r.EffectOptions["--starting-color"]).Original);
    }
}
