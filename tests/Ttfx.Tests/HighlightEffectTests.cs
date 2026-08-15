using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class HighlightEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("highlight DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("highlight defaults", DefaultOptions);
        yield return new TestCase("highlight flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Highlight(new HighlightConfig());
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
        ParseResult r = CliParser.Parse(["highlight"]);
        Harness.AssertEqual("brightness", 1.75, (double)r.EffectOptions["--highlight-brightness"]);
        Harness.AssertEqual(
            "direction",
            CharacterGroup.DiagonalBottomLeftToTopRight,
            (CharacterGroup)r.EffectOptions["--highlight-direction"]);
        Harness.AssertEqual("width", 8L, (long)r.EffectOptions["--highlight-width"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "highlight",
                "--highlight-brightness",
                "2.5",
                "--highlight-direction",
                "center_to_outside",
                "--highlight-width",
                "3",
                "--final-gradient-stops",
                "ff0000",
                "0000ff",
                "--final-gradient-steps",
                "6",
                "--final-gradient-direction",
                "horizontal",
            ]);
        Harness.AssertEqual("user brightness", 2.5, (double)r.EffectOptions["--highlight-brightness"]);
        Harness.AssertEqual(
            "user direction",
            CharacterGroup.CenterToOutside,
            (CharacterGroup)r.EffectOptions["--highlight-direction"]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Horizontal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
