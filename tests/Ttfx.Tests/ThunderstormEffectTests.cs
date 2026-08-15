using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class ThunderstormEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("thunderstorm DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("thunderstorm AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("thunderstorm flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Thunderstorm(new ThunderstormConfig
        {
            RaindropSymbols = ["\\", ".", ","],
            SparkSymbols = ["*", ".", "'"],
        });
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
        effect.DispatchCallback(world, new CharId(0), new EffectCallback(5, []));
        Harness.AssertTrue("dispatch runs", true);
    }

    private static void DefaultOptions()
    {
        ParseResult r = CliParser.Parse(["thunderstorm"]);
        Harness.AssertEqual("storm time", 12L, r.EffectOptions["--storm-time"]);
        Harness.AssertEqual("lightning color", "68A3E8", ((Color)r.EffectOptions["--lightning-color"]).Original);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(["thunderstorm", "--storm-time", "2", "--lightning-color", "ffff00"]);
        Harness.AssertEqual("user storm time", 2L, r.EffectOptions["--storm-time"]);
        Harness.AssertEqual("user lightning color", "ffff00", ((Color)r.EffectOptions["--lightning-color"]).Original);
    }
}
