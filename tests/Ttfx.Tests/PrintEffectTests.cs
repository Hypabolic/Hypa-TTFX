using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class PrintEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("print DispatchCallback hides character", DispatchCallback);
        yield return new TestCase("print defaults", DefaultOptions);
    }

    private static void DispatchCallback()
    {
        var effect = new PrintEffect(new PrintEffectConfig());
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
        ParseResult r = CliParser.Parse(["print"]);
        Harness.AssertEqual("return speed", 1.5, (double)r.EffectOptions["--print-head-return-speed"]);
        Harness.AssertEqual("print speed", 2L, (long)r.EffectOptions["--print-speed"]);
        Harness.AssertEqual(
            "head easing",
            Easing.InOutQuad,
            (Easing)r.EffectOptions["--print-head-easing"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Diagonal,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }
}
