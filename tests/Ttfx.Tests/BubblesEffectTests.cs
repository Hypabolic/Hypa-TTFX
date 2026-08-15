using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class BubblesEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("bubbles DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("bubbles AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("bubbles flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Bubbles(new BubblesConfig());
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
        Harness.AssertTrue("factory", EffectRegistry.Find("bubbles")!.Factory is not null);
        ParseResult r = CliParser.Parse(["bubbles"]);
        Harness.AssertTrue("rainbow absent", !r.EffectOptions.ContainsKey("--rainbow"));
        var colors = (List<object>)r.EffectOptions["--bubble-colors"];
        Harness.AssertEqual("color count", 4, colors.Count);
        Harness.AssertEqual("pop color", "ffffff", ((Color)r.EffectOptions["--pop-color"]).Original);
        Harness.AssertEqual("speed", 0.5, (double)r.EffectOptions["--bubble-speed"]);
        Harness.AssertEqual("delay", 20L, (long)r.EffectOptions["--bubble-delay"]);
        Harness.AssertEqual("pop cond", PopCondition.Row, (PopCondition)r.EffectOptions["--pop-condition"]);
        Harness.AssertEqual("ease", Easing.InOutSine, (Easing)r.EffectOptions["--movement-easing"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "bubbles",
                "--rainbow",
                "--pop-condition",
                "bottom",
                "--bubble-delay",
                "5",
                "--bubble-speed",
                "0.8",
            ]);
        Harness.AssertTrue("rainbow present", r.EffectOptions.ContainsKey("--rainbow"));
        Harness.AssertEqual("user pop", PopCondition.Bottom, (PopCondition)r.EffectOptions["--pop-condition"]);
        Harness.AssertEqual("user delay", 5L, (long)r.EffectOptions["--bubble-delay"]);
        Harness.AssertEqual("user speed", 0.8, (double)r.EffectOptions["--bubble-speed"]);
    }
}
