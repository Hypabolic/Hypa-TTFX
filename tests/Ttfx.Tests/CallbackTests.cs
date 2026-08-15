using System.Collections.Generic;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

/// <summary>
/// The only two <c>DispatchCallback</c> implementations whose unit tests
/// observe a mutation. Empty/no-op dispatch bodies are covered by parity dumps.
/// </summary>
internal static class CallbackTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("rings DispatchCallback hides character", RingsHides);
        yield return new TestCase("print DispatchCallback hides character", PrintHides);
    }

    private static void RingsHides()
    {
        AssertHides(new Rings(new RingsConfig()));
    }

    private static void PrintHides()
    {
        AssertHides(new PrintEffect(new PrintEffectConfig()));
    }

    private static void AssertHides(IEffect effect)
    {
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
}
