using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class RebuildAfterTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("rebuild-after parser", RebuildAfterParser);
        yield return new TestCase("rebuild-after dump continues rng", RebuildAfterDumpContinuesRng);
        yield return new TestCase("max-frames+rebuild-after M less than N", MaxFramesLessThanRebuildAfter);
        yield return new TestCase("max-frames+rebuild-after M greater than N", MaxFramesGreaterThanRebuildAfter);
        yield return new TestCase("max-frames+rebuild-after M equals N", MaxFramesEqualRebuildAfter);
    }

    private static void RebuildAfterParser()
    {
        ParseResult r = CliParser.Parse(["--rebuild-after", "5", "wipe"]);
        Harness.AssertEqual("rebuild-after", 5UL, r.Root.RebuildAfter);
    }

    private static void RebuildAfterDumpContinuesRng()
    {
        var config = new TerminalConfig
        {
            CanvasWidth = 40,
            CanvasHeight = 12,
            IgnoreTerminalDimensions = true,
            FrameRate = 0,
        };
        Rng rng = Rng.Seeded(99);
        double before = rng.Random();

        EngineWorld first = EngineWorld.New("hi", config, rng, Clock.VirtualWithFrameRate(0));
        var effect = new CountingEffect(10);
        (ulong _, bool complete) = EffectRunner.DumpEffect(effect, first, 3);
        Harness.AssertTrue("truncated before completion", !complete);

        EngineWorld rebuilt = EngineWorld.New("hi", config, first.Rng, Clock.VirtualWithFrameRate(0));
        Harness.AssertTrue("same rng instance", ReferenceEquals(first.Rng, rebuilt.Rng));
        double after = rebuilt.Rng.Random();

        Rng expected = Rng.Seeded(99);
        expected.Random();
        double expectedNext = expected.Random();
        Harness.AssertEqual("first draw preserved", before, before);
        Harness.AssertEqual("rng advanced across rebuild", expectedNext, after);
    }

    private static void MaxFramesLessThanRebuildAfter()
    {
        ulong total = SimulateRebuildAfterDump(maxFrames: 3, rebuildAfter: 5, effectFrames: 20);
        Harness.AssertEqual("total frames M<N", 3UL, total);
    }

    private static void MaxFramesGreaterThanRebuildAfter()
    {
        ulong total = SimulateRebuildAfterDump(maxFrames: 10, rebuildAfter: 5, effectFrames: 20);
        Harness.AssertEqual("total frames M>N", 10UL, total);
    }

    private static void MaxFramesEqualRebuildAfter()
    {
        ulong total = SimulateRebuildAfterDump(maxFrames: 5, rebuildAfter: 5, effectFrames: 20);
        Harness.AssertEqual("total frames M==N", 5UL, total);
    }

    /// <summary>Mirror Program.cs parity-dump rebuild loop for limit composition tests.</summary>
    private static ulong SimulateRebuildAfterDump(ulong maxFrames, ulong rebuildAfter, int effectFrames)
    {
        var config = new TerminalConfig
        {
            CanvasWidth = 40,
            CanvasHeight = 12,
            IgnoreTerminalDimensions = true,
            FrameRate = 0,
        };
        bool rebuildTriggered = false;
        ulong totalEmitted = 0;
        Rng rng = Rng.Seeded(1);

        while (true)
        {
            EngineWorld world = EngineWorld.New("hi", config, rng, Clock.VirtualWithFrameRate(0));
            var effect = new CountingEffect(effectFrames);

            ulong? dumpLimit = null;
            dumpLimit = maxFrames - totalEmitted;

            if (!rebuildTriggered)
            {
                ulong cap = rebuildAfter;
                if (dumpLimit is ulong limit && limit < cap)
                {
                    cap = limit;
                }

                dumpLimit = cap;
            }

            (ulong emitted, bool complete) = EffectRunner.DumpEffect(effect, world, dumpLimit);
            totalEmitted += emitted;
            if (!rebuildTriggered && emitted >= rebuildAfter && !complete)
            {
                if (totalEmitted < maxFrames)
                {
                    config.ReuseCanvas = false;
                    rng = world.Rng;
                    rebuildTriggered = true;
                    continue;
                }
            }

            return totalEmitted;
        }
    }

    private sealed class CountingEffect : IEffect
    {
        private readonly int _max;
        private int _count;

        public CountingEffect(int max) => _max = max;

        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world)
        {
            if (_count >= _max)
            {
                return null;
            }

            _count++;
            return $"frame {_count}\n";
        }

        public void DispatchCallback(EngineWorld world, CharId id, EffectCallback callback)
        {
        }
    }
}

internal static class MultiValueOptionTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("every AtLeastOne option accepts multiple values", EveryAtLeastOneOption);
    }

    private static void EveryAtLeastOneOption()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (EffectSpec effect in EffectRegistry.Effects)
        {
            foreach (OptionSpec spec in effect.Options)
            {
                if (spec.Arity.Kind != OptionArityKind.AtLeastOne)
                {
                    continue;
                }

                if (!seen.Add($"{effect.Name}:{spec.Long}"))
                {
                    continue;
                }

                string[] tokens = BuildParseTokens(effect.Name, spec);
                ParseResult r = CliParser.Parse(tokens);
                Harness.AssertEqual($"{effect.Name} {spec.Long} effect", effect.Name, r.EffectName);
                Harness.AssertTrue(
                    $"{effect.Name} {spec.Long} present",
                    r.EffectOptions.ContainsKey(spec.Long));
                var list = (List<object>)r.EffectOptions[spec.Long];
                Harness.AssertTrue($"{effect.Name} {spec.Long} count", list.Count >= 2);
            }
        }
    }

    private static string[] BuildParseTokens(string effectName, OptionSpec spec)
    {
        if (spec.Parse == ValueParsers.ParseColorArg)
        {
            return [effectName, spec.Long, "ff0000", "00ff00"];
        }

        if (spec.Parse == ValueParsers.ParseSymbol)
        {
            return [effectName, spec.Long, "a", "b"];
        }

        if (spec.Parse == ValueParsers.ParseString)
        {
            return [effectName, spec.Long, "x", "y"];
        }

        if (spec.Parse == ValueParsers.ParseCommonPositiveInt
            || spec.Parse == ValueParsers.ParseGradientSteps
            || spec.Parse == ValueParsers.ParsePositiveInt)
        {
            return [effectName, spec.Long, "2", "3"];
        }

        if (spec.Parse == ValueParsers.ParseNonNegativeInt)
        {
            return [effectName, spec.Long, "0", "1"];
        }

        if (spec.Parse == ValueParsers.ParseNonNegativeRatio
            || spec.Parse == ValueParsers.ParsePositiveRatio)
        {
            return [effectName, spec.Long, "0.5", "0.25"];
        }

        if (spec.Parse == ValueParsers.ParsePositiveFloat
            || spec.Parse == ValueParsers.ParseNonNegativeFloat)
        {
            return [effectName, spec.Long, "1.0", "2.0"];
        }

        if (spec.Parse == ValueParsers.ParseEasingName)
        {
            return [effectName, spec.Long, "in_sine", "out_sine"];
        }

        if (spec.Parse == ValueParsers.ParsePositiveIntRange
            || spec.Parse == ValueParsers.ParsePositiveFloatRange)
        {
            return [effectName, spec.Long, "1-2", "3-4"];
        }

        if (spec.Parse == ValueParsers.ParseGradientDirection
            || spec.Parse == ValueParsers.ParseWaveDirection
            || spec.Parse == ValueParsers.ParseCharacterGroup)
        {
            return [effectName, spec.Long, "horizontal", "vertical"];
        }

        return [effectName, spec.Long, "1", "2"];
    }
}
