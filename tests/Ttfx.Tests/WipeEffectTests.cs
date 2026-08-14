using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class WipeEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("wipe DispatchCallback is callable", WipeDispatchCallback);
        yield return new TestCase("registry 37 names in EffectCommand order", RegistryThirtySevenNames);
        yield return new TestCase("probe is not a registry name", ProbeAbsentFromRegistry);
        yield return new TestCase("virtual clock frame-rate 0 uses dt=1/60", VirtualClockDt);
        yield return new TestCase("clock advances inside Frame not the run loop", ClockAdvancesInsideFrame);
        yield return new TestCase("max-frames 0 emits one frame", MaxFramesZeroEmitsOne);
        yield return new TestCase("max-frames 1 emits one frame", MaxFramesOneEmitsOne);
        yield return new TestCase("max-frames past completion emits all", MaxFramesPastCompletionEmitsAll);
        yield return new TestCase("run_effect restores cursor on error", RestoreCursorOnError);
        yield return new TestCase("wipe AtLeastOne defaults when flag absent", WipeDefaultStops);
        yield return new TestCase("wipe stops flag replaces defaults", WipeStopsReplaceDefaults);
        yield return new TestCase("DEC save/restore are ESC plus digit", DecSaveRestoreBytes);
    }

    private static void WipeDispatchCallback()
    {
        var wipe = new Wipe(new WipeConfig());
        EngineWorld world = MakeWorld();
        wipe.DispatchCallback(world, new CharId(0), new EffectCallback(0, []));
        Harness.AssertTrue("dispatch is a no-op", true);
    }

    private static void RegistryThirtySevenNames()
    {
        string[] expected =
        [
            "beams", "binarypath", "blackhole", "bouncyballs", "bubbles", "burn",
            "colorshift", "crumble", "decrypt", "errorcorrect", "expand", "fireworks",
            "highlight", "laseretch", "matrix", "middleout", "orbittingvolley", "overflow",
            "pour", "print", "rain", "randomsequence", "rings", "scattered",
            "slice", "slide", "smoke", "spotlights", "spray", "swarm",
            "sweep", "synthgrid", "thunderstorm", "unstable", "vhstape", "waves",
            "wipe",
        ];
        Harness.AssertEqual("count", 37, EffectRegistry.Effects.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Harness.AssertEqual($"name[{i}]", expected[i], EffectRegistry.Effects[i].Name);
        }

        Harness.AssertTrue("wipe factory", EffectRegistry.Find("wipe")!.Factory is not null);
        Harness.AssertTrue("bouncyballs factory", EffectRegistry.Find("bouncyballs")!.Factory is not null);
        Harness.AssertTrue("errorcorrect factory", EffectRegistry.Find("errorcorrect")!.Factory is not null);
        Harness.AssertTrue("expand factory", EffectRegistry.Find("expand")!.Factory is not null);
        Harness.AssertTrue("middleout factory", EffectRegistry.Find("middleout")!.Factory is not null);
        Harness.AssertTrue("beams factory null", EffectRegistry.Find("beams")!.Factory is null);
    }

    private static void ProbeAbsentFromRegistry()
    {
        Harness.AssertTrue("no probe", !EffectRegistry.Contains("probe"));
        Harness.AssertEqual("still 37", 37, EffectRegistry.Effects.Length);
    }

    private static void VirtualClockDt()
    {
        var zero = (Clock.Virtual)Clock.VirtualWithFrameRate(0);
        Harness.AssertEqual("dt at 0", 1.0 / 60.0, zero.Dt);
        var negative = (Clock.Virtual)Clock.VirtualWithFrameRate(-1);
        Harness.AssertEqual("dt at -1", 1.0 / 60.0, negative.Dt);
        var sixty = (Clock.Virtual)Clock.VirtualWithFrameRate(60);
        Harness.AssertEqual("dt at 60", 1.0 / 60.0, sixty.Dt);
        var thirty = (Clock.Virtual)Clock.VirtualWithFrameRate(30);
        Harness.AssertEqual("dt at 30", 1.0 / 30.0, thirty.Dt);
    }

    private static void ClockAdvancesInsideFrame()
    {
        EngineWorld world = MakeWorld();
        var virt = (Clock.Virtual)world.Clock;
        Harness.AssertEqual("start", 0.0, virt.Now);
        var effect = new DoubleFrameEffect();
        effect.Build(world);
        string? frame = effect.NextFrame(world);
        Harness.AssertTrue("returned second frame", frame is not null);
        Harness.AssertEqual("advanced 2*dt", 2.0 * virt.Dt, virt.Now);
    }

    private static void MaxFramesZeroEmitsOne()
    {
        ulong count = DumpCounting(0, out string stderr);
        Harness.AssertEqual("count", 1UL, count);
        Harness.AssertTrue("stderr", stderr.Contains("frames=1", StringComparison.Ordinal));
    }

    private static void MaxFramesOneEmitsOne()
    {
        ulong count = DumpCounting(1, out string stderr);
        Harness.AssertEqual("count", 1UL, count);
        Harness.AssertTrue("stderr", stderr.Contains("frames=1", StringComparison.Ordinal));
    }

    private static void MaxFramesPastCompletionEmitsAll()
    {
        ulong count = DumpCounting(100, out string stderr);
        Harness.AssertEqual("count", 3UL, count);
        Harness.AssertTrue("stderr", stderr.Contains("frames=3", StringComparison.Ordinal));
    }

    private static void RestoreCursorOnError()
    {
        EngineWorld world = MakeWorld();
        using var stdout = new MemoryStream();
        try
        {
            EffectRunner.RunEffect(new ThrowingEffect(), world, stdout);
            Harness.AssertTrue("should throw", false);
        }
        catch (EngineException)
        {
            byte[] bytes = stdout.ToArray();
            string text = Encoding.UTF8.GetString(bytes);
            Harness.AssertTrue("hid cursor", text.Contains(Ansi.HideCursor, StringComparison.Ordinal));
            Harness.AssertTrue("showed cursor", text.Contains(Ansi.ShowCursor, StringComparison.Ordinal));
            Harness.AssertTrue("eol", text.EndsWith('\n'));
        }
    }

    private static void WipeDefaultStops()
    {
        ParseResult r = CliParser.Parse(["wipe"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("stop count", 3, stops.Count);
        Harness.AssertEqual("stop0", "833ab4", ((Color)stops[0]).Original);
        Harness.AssertEqual("stop1", "fd1d1d", ((Color)stops[1]).Original);
        Harness.AssertEqual("stop2", "fcb045", ((Color)stops[2]).Original);
        var steps = (List<object>)r.EffectOptions["--final-gradient-steps"];
        Harness.AssertEqual("steps count", 1, steps.Count);
        Harness.AssertEqual("steps0", 12L, (long)steps[0]);
        Harness.AssertEqual("ease", Easing.InOutCirc, (Easing)r.EffectOptions["--wipe-ease"]);
        Harness.AssertEqual("delay", 0L, (long)r.EffectOptions["--wipe-delay"]);
        Harness.AssertEqual("frames", 3L, (long)r.EffectOptions["--final-gradient-frames"]);
        Harness.AssertEqual(
            "direction",
            CharacterGroup.DiagonalTopLeftToBottomRight,
            (CharacterGroup)r.EffectOptions["--wipe-direction"]);
        Harness.AssertEqual(
            "grad dir",
            GradientDirection.Vertical,
            (GradientDirection)r.EffectOptions["--final-gradient-direction"]);
    }

    private static void DecSaveRestoreBytes()
    {
        Harness.AssertEqual("save", "\u001b7", Ansi.DecSaveCursor);
        Harness.AssertEqual("restore", "\u001b8", Ansi.DecRestoreCursor);
        Harness.AssertEqual("save byte0", 0x1b, (int)Ansi.DecSaveCursor[0]);
        Harness.AssertEqual("save byte1", (int)'7', (int)Ansi.DecSaveCursor[1]);
    }

    private static void WipeStopsReplaceDefaults()
    {
        ParseResult r = CliParser.Parse(["wipe", "--final-gradient-stops", "ff0000"]);
        var stops = (List<object>)r.EffectOptions["--final-gradient-stops"];
        Harness.AssertEqual("user stop count", 1, stops.Count);
        Harness.AssertEqual("user stop", "ff0000", ((Color)stops[0]).Original);
    }

    private static ulong DumpCounting(ulong? maxFrames, out string stderr)
    {
        EngineWorld world = MakeWorld();
        using var stdout = new MemoryStream();
        var err = new StringWriter();
        ulong count = EffectRunner.DumpEffect(new ThreeFrameEffect(), world, maxFrames, stdout, err);
        stderr = err.ToString();
        return count;
    }

    private static EngineWorld MakeWorld()
    {
        var config = new TerminalConfig
        {
            CanvasWidth = 20,
            CanvasHeight = 8,
            IgnoreTerminalDimensions = true,
            FrameRate = 0,
        };
        return EngineWorld.New("hi", config, Rng.Seeded(1), Clock.VirtualWithFrameRate(0));
    }

    private sealed class DoubleFrameEffect : IEffect
    {
        private int _calls;

        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world)
        {
            if (_calls > 0)
            {
                return null;
            }

            _calls += 1;
            world.Frame();
            return world.Frame();
        }

        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
        }
    }

    private sealed class ThreeFrameEffect : IEffect
    {
        private int _n;

        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world)
        {
            if (_n >= 3)
            {
                return null;
            }

            _n += 1;
            return "f";
        }

        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
        }
    }

    private sealed class ThrowingEffect : IEffect
    {
        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world) => throw new EngineException("boom");

        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
        }
    }
}
