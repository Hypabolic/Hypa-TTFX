using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class MatrixEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("matrix DispatchCallback is callable", DispatchCallback);
        yield return new TestCase("matrix AtLeastOne defaults when flag absent", DefaultOptions);
        yield return new TestCase("matrix flags replace defaults", FlagsReplaceDefaults);
    }

    private static void DispatchCallback()
    {
        var effect = new Matrix(new MatrixConfig());
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
        ParseResult r = CliParser.Parse(["matrix"]);
        Harness.AssertEqual("highlight color", "dbffdb", ((Color)r.EffectOptions["--highlight-color"]).Original);
        var rainColors = (List<object>)r.EffectOptions["--rain-color-gradient"];
        Harness.AssertEqual("rain color count", 2, rainColors.Count);
        var rainSymbols = (List<object>)r.EffectOptions["--rain-symbols"];
        Harness.AssertEqual("rain symbol count", 50, rainSymbols.Count);
        Harness.AssertEqual("rain time", 15L, (long)r.EffectOptions["--rain-time"]);
        Harness.AssertEqual("resolve delay", 3L, (long)r.EffectOptions["--resolve-delay"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Radial,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void FlagsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(
            [
                "matrix",
                "--rain-time",
                "2",
                "--highlight-color",
                "ffffff",
                "--resolve-delay",
                "1",
                "--final-gradient-direction",
                "vertical",
            ]);
        Harness.AssertEqual("user rain time", 2L, (long)r.EffectOptions["--rain-time"]);
        Harness.AssertEqual("user resolve delay", 1L, (long)r.EffectOptions["--resolve-delay"]);
        Harness.AssertEqual(
            "user grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
