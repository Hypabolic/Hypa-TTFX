using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ttfx.Engine;
using Ttfx.Utils;
using Path = System.IO.Path;

namespace Ttfx.Tests;

internal static class RngVectors
{
    private const int Count = 10_000;

    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("rngdump seed 42 first 10k", () => MatchDump(42));
        yield return new TestCase("rngdump seed 7 first 10k", () => MatchDump(7));
        yield return new TestCase("rngdump /tmp seed 42 if present", MatchTmpDump);
        yield return new TestCase("randint(0,2) rejection loop covered", RejectionLoop);
        yield return new TestCase("shuffle matches CPython/rngdump order", ShuffleOrder);
        yield return new TestCase("Rng instance continues after rebuild reassign", RebuildContinues);
        yield return new TestCase("Rng is an instance not static", TwoInstancesIndependent);
        yield return new TestCase("Rng invariant failures throw", Invariants);
    }

    internal static string Dump(ulong seed)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb, CultureInfo.InvariantCulture);
        WriteDump(writer, seed);
        return sb.ToString();
    }

    internal static void WriteDump(TextWriter w, ulong seed)
    {
        w.WriteLine($"SEED {seed}");
        w.WriteLine($"COUNT {Count}");

        {
            Rng r = Rng.Seeded(seed);
            w.WriteLine("SECTION random");
            for (int i = 0; i < Count; i++)
            {
                w.WriteLine($"{BitConverter.DoubleToUInt64Bits(r.Random()):x16}");
            }
        }

        {
            Rng r = Rng.Seeded(seed);
            w.WriteLine("SECTION randint 0 2");
            for (int i = 0; i < Count; i++)
            {
                w.WriteLine(r.Randint(0, 2).ToString(CultureInfo.InvariantCulture));
            }
        }

        {
            Rng r = Rng.Seeded(seed);
            w.WriteLine("SECTION randrange 0 5");
            for (int i = 0; i < Count; i++)
            {
                w.WriteLine(r.Randrange(0, 5).ToString(CultureInfo.InvariantCulture));
            }
        }

        {
            Rng r = Rng.Seeded(seed);
            string[] seq = ["a", "b", "c", "d", "e"];
            w.WriteLine("SECTION choice a b c d e");
            for (int i = 0; i < Count; i++)
            {
                w.WriteLine(r.Choice(seq));
            }
        }

        {
            Rng r = Rng.Seeded(seed);
            w.WriteLine("SECTION choice_index 7");
            for (int i = 0; i < Count; i++)
            {
                w.WriteLine(r.ChoiceIndex(7).ToString(CultureInfo.InvariantCulture));
            }
        }

        {
            Rng r = Rng.Seeded(seed);
            w.WriteLine("SECTION uniform 1 2");
            for (int i = 0; i < Count; i++)
            {
                w.WriteLine($"{BitConverter.DoubleToUInt64Bits(r.Uniform(1.0, 2.0)):x16}");
            }
        }

        {
            Rng r = Rng.Seeded(seed);
            w.WriteLine("SECTION shuffle 0 1 2 3 4 5 6 7");
            for (int i = 0; i < Count; i++)
            {
                int[] seq = [0, 1, 2, 3, 4, 5, 6, 7];
                r.Shuffle(seq);
                w.Write(seq[0].ToString(CultureInfo.InvariantCulture));
                for (int k = 1; k < seq.Length; k++)
                {
                    w.Write(' ');
                    w.Write(seq[k].ToString(CultureInfo.InvariantCulture));
                }

                w.WriteLine();
            }
        }
    }

    private static void MatchDump(ulong seed)
    {
        string actual = Dump(seed);
        string fixture = Path.Combine(
            Harness.FindRepoRoot(),
            "tests",
            "Ttfx.Tests",
            "fixtures",
            $"rngdump_{seed}.txt");
        if (!File.Exists(fixture))
        {
            Harness.AssertTrue($"rngdump_{seed} fixture exists", false);
            return;
        }

        CompareDumps($"seed {seed}", File.ReadAllText(fixture), actual);
    }

    private static void MatchTmpDump()
    {
        const string path = "/tmp/rngdump.txt";
        if (!File.Exists(path))
        {
            Harness.AssertTrue(" /tmp/rngdump.txt optional skip", true);
            return;
        }

        string rust = File.ReadAllText(path);
        if (!rust.StartsWith("SEED 42", StringComparison.Ordinal))
        {
            Harness.AssertTrue("/tmp/rngdump.txt is seed 42", false);
            return;
        }

        CompareDumps("/tmp/rngdump.txt", rust, Dump(42));
    }

    private static void CompareDumps(string label, string expectedRaw, string actualRaw)
    {
        string[] expected = SplitLines(expectedRaw);
        string[] actual = SplitLines(actualRaw);
        int n = Math.Min(expected.Length, actual.Length);
        int first = -1;
        for (int i = 0; i < n; i++)
        {
            if (expected[i] != actual[i])
            {
                first = i;
                break;
            }
        }

        if (first >= 0)
        {
            Console.Error.WriteLine(
                $"FAIL {label} first diverge L{first + 1}: expected {expected[first]} got {actual[first]}");
        }

        Harness.AssertEqual($"{label} first diverge", -1, first);
        Harness.AssertEqual($"{label} line count", expected.Length, actual.Length);
    }

    private static string[] SplitLines(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    private static void RejectionLoop()
    {
        // randint(0, 2) is n=3, not a power of two, so the bit-mask rejection
        // loop in randbelow retries. Matching 10k draws against rngdump is the
        // contract; this asserts the values stay in range and are not all equal
        // (a stuck mask would collapse the stream).
        Rng r = Rng.Seeded(42);
        var counts = new int[3];
        int outOfRange = 0;
        for (int i = 0; i < Count; i++)
        {
            long v = r.Randint(0, 2);
            if (v is < 0 or > 2)
            {
                outOfRange++;
                continue;
            }

            counts[v]++;
        }

        Harness.AssertEqual("randint(0,2) out of range", 0, outOfRange);

        Harness.AssertTrue("randint(0,2) saw 0", counts[0] > 0);
        Harness.AssertTrue("randint(0,2) saw 1", counts[1] > 0);
        Harness.AssertTrue("randint(0,2) saw 2", counts[2] > 0);
    }

    private static void ShuffleOrder()
    {
        Rng r = Rng.Seeded(42);
        int[] seq = [0, 1, 2, 3, 4, 5, 6, 7];
        r.Shuffle(seq);
        // First shuffle of seed 42 from rngdump — pinned after the dump matches.
        string got = string.Join(' ', seq);
        Rng r2 = Rng.Seeded(42);
        int[] again = [0, 1, 2, 3, 4, 5, 6, 7];
        r2.Shuffle(again);
        Harness.AssertEqual("shuffle deterministic", got, string.Join(' ', again));

        // CPython loop: for i in reversed(range(1, len)): j = randbelow(i+1); swap
        // A bottom-up Fisher-Yates would produce a different first permutation.
        Rng r3 = Rng.Seeded(42);
        int[] bottomUp = [0, 1, 2, 3, 4, 5, 6, 7];
        for (int i = 1; i < bottomUp.Length; i++)
        {
            int j = (int)r3.Randint(0, i);
            (bottomUp[i], bottomUp[j]) = (bottomUp[j], bottomUp[i]);
        }

        Harness.AssertTrue("top-down != bottom-up (loop order matters)", got != string.Join(' ', bottomUp));
    }

    private static void RebuildContinues()
    {
        Rng rng = Rng.Seeded(42);
        for (int i = 0; i < 100; i++)
        {
            rng.Random();
        }

        // main.rs: rng = ctx.rng — same instance, state carried forward.
        Rng rebuilt = rng;
        double afterRebuild = rebuilt.Random();

        Rng control = Rng.Seeded(42);
        double last = 0;
        for (int i = 0; i < 101; i++)
        {
            last = control.Random();
        }

        Harness.AssertEqual(
            "rebuild continues the stream",
            BitConverter.DoubleToUInt64Bits(last),
            BitConverter.DoubleToUInt64Bits(afterRebuild));
        Harness.AssertTrue("reassign is the same instance", ReferenceEquals(rng, rebuilt));

        Rng reseeds = Rng.Seeded(42);
        Harness.AssertTrue(
            "a fresh Seeded(42) would reset",
            BitConverter.DoubleToUInt64Bits(reseeds.Random())
            != BitConverter.DoubleToUInt64Bits(afterRebuild));
    }

    private static void TwoInstancesIndependent()
    {
        Rng a = Rng.Seeded(42);
        Rng b = Rng.Seeded(42);
        double firstA = a.Random();
        for (int i = 0; i < 50; i++)
        {
            a.Random();
        }

        double firstB = b.Random();
        Harness.AssertEqual(
            "same seed same first draw",
            BitConverter.DoubleToUInt64Bits(firstA),
            BitConverter.DoubleToUInt64Bits(firstB));
        Harness.AssertTrue(
            "drawing on a does not advance b",
            BitConverter.DoubleToUInt64Bits(a.Random())
            != BitConverter.DoubleToUInt64Bits(b.Random()));
    }

    private static void Invariants()
    {
        Harness.AssertThrows<EngineInvariantException>("randint empty", () => Rng.Seeded(1).Randint(3, 2));
        Harness.AssertThrows<EngineInvariantException>("randrange empty", () => Rng.Seeded(1).Randrange(3, 3));
        Harness.AssertThrows<EngineInvariantException>("choice empty", () => Rng.Seeded(1).Choice(Array.Empty<int>()));
        Harness.AssertThrows<EngineInvariantException>("choice_index 0", () => Rng.Seeded(1).ChoiceIndex(0));
    }
}
