using System;
using System.Collections.Generic;

namespace Ttfx.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<TestCase>();
        tests.AddRange(CliParserTests.All());
        tests.AddRange(ValueParserTests.All());
        tests.AddRange(NumericCorpusTests.All());
        tests.AddRange(ReflectionGuardTests.All());
        tests.AddRange(M0FrameTests.All());
        tests.AddRange(GraphicsTests.All());
        tests.AddRange(ColorPipelineTests.All());
        tests.AddRange(UnicodeTests.All());
        tests.AddRange(PyCompatTests.All());
        tests.AddRange(RngVectors.All());
        tests.AddRange(EasingGeometryGoldens.All());
        tests.AddRange(EngineTraces.All());
        tests.AddRange(TerminalGroupingTests.All());
        tests.AddRange(TickMachineryTests.All());
        tests.AddRange(WavesEffectTests.All());
        tests.AddRange(UnstableEffectTests.All());
        tests.AddRange(SweepEffectTests.All());
        tests.AddRange(SwarmEffectTests.All());
        tests.AddRange(SpotlightsEffectTests.All());
        tests.AddRange(RingsEffectTests.All());
        tests.AddRange(PrintEffectTests.All());
        tests.AddRange(OverflowEffectTests.All());
        tests.AddRange(OrbittingVolleyEffectTests.All());
        tests.AddRange(HighlightEffectTests.All());
        tests.AddRange(FireworksEffectTests.All());
        tests.AddRange(DecryptEffectTests.All());
        tests.AddRange(CrumbleEffectTests.All());
        tests.AddRange(ColorShiftEffectTests.All());
        tests.AddRange(BubblesEffectTests.All());
        tests.AddRange(BlackholeEffectTests.All());
        tests.AddRange(BinaryPathEffectTests.All());
        tests.AddRange(BeamsEffectTests.All());
        tests.AddRange(WipeEffectTests.All());
        tests.AddRange(BouncyBallsEffectTests.All());
        tests.AddRange(ErrorCorrectEffectTests.All());
        tests.AddRange(ExpandEffectTests.All());
        tests.AddRange(MiddleoutEffectTests.All());
        tests.AddRange(PourEffectTests.All());
        tests.AddRange(RainEffectTests.All());
        tests.AddRange(RandomSequenceEffectTests.All());
        tests.AddRange(ScatteredEffectTests.All());
        tests.AddRange(SliceEffectTests.All());
        tests.AddRange(SlideEffectTests.All());
        tests.AddRange(SprayEffectTests.All());
        tests.AddRange(SignalsResizeTests.All());

        foreach (TestCase test in tests)
        {
            try
            {
                test.Run();
            }
            catch (Exception ex)
            {
                Harness.Failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
            }
        }

        Console.WriteLine($"tests: {Harness.Passes} passed, {Harness.Failures} failed");
        return Harness.Failures == 0 ? 0 : 1;
    }
}
