using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Ttfx;
using Ttfx.Utils;

namespace Ttfx.Tests;

/// <summary>
/// Goldens asserted against the AOT-published <c>artifacts/ttfx</c>, not
/// <c>dotnet run</c>. Tolerances match <c>easing_goldens.rs</c>: bit-exact on
/// Linux/glibc except CubicBezier (1 ulp); 1e-15 absolute elsewhere.
/// </summary>
internal static class EasingGeometryGoldens
{
    private const int EasingCount = 34;
    private const int SamplesPerEasing = 1001;
    private const int EasingBytes = EasingCount * SamplesPerEasing * 8;

    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("AOT easing goldens 31+MakeEasing", EasingGoldensFromAot);
        yield return new TestCase("AOT geometry goldens", GeometryGoldensFromAot);
        yield return new TestCase("bezier arc length omits t=0.9..1.0", BezierLengthTruncated);
        yield return new TestCase("normalized distance rejects OOB", NormalizedDistanceOob);
        yield return new TestCase("doubled row delta at line and bezier", DoubledRowDeltas);
        yield return new TestCase("expo and elastic endpoint guards", ExpoElasticGuards);
    }

    private static void EasingGoldensFromAot()
    {
        byte[] expected = File.ReadAllBytes(
            Path.Combine(Harness.FindRepoRoot(), "tests", "Ttfx.Tests", "fixtures", "easing_goldens.bin"));
        Harness.AssertEqual("easing fixture size", EasingBytes, expected.Length);

        byte[] actual = RunPublished("--easing-golden-dump", binary: true);
        Harness.AssertEqual("easing dump size", EasingBytes, actual.Length);

        bool linuxGnu = OperatingSystem.IsLinux();
        int mismatches = 0;
        int offset = 0;
        for (int e = 0; e < EasingCount; e++)
        {
            bool cubicBezier = e >= 31;
            for (int i = 0; i <= 1000; i++)
            {
                double exp = BitConverter.ToDouble(expected, offset);
                double act = BitConverter.ToDouble(actual, offset);
                offset += 8;
                bool within = linuxGnu
                    ? UlpDiff(act, exp) <= (cubicBezier ? 1UL : 0UL)
                    : Math.Abs(act - exp) <= 1e-15;
                if (!within)
                {
                    if (mismatches < 5)
                    {
                        double p = i / 1000.0;
                        Console.Error.WriteLine(
                            $"easing[{e}] p={p.ToString(CultureInfo.InvariantCulture)}: expected {exp} ({DoubleToUInt64Bits(exp):x16}), got {act} ({DoubleToUInt64Bits(act):x16})");
                    }

                    mismatches++;
                }
            }
        }

        Harness.AssertEqual("easing golden mismatches", 0, mismatches);
    }

    private static void GeometryGoldensFromAot()
    {
        string fixture = File.ReadAllText(
            Path.Combine(Harness.FindRepoRoot(), "tests", "Ttfx.Tests", "fixtures", "geometry_goldens.txt"));
        string[] expected = SplitLines(fixture);
        string dump = Encoding.UTF8.GetString(RunPublished("--geometry-golden-dump", binary: false));
        string[] actual = SplitLines(dump);
        Harness.AssertEqual("geometry golden line count", expected.Length, actual.Length);

        bool linuxGnu = OperatingSystem.IsLinux();
        int mismatches = 0;
        int n = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < n; i++)
        {
            if (GeometryLineMatches(expected[i], actual[i], linuxGnu))
            {
                continue;
            }

            if (mismatches < 5)
            {
                Console.Error.WriteLine($"expected: {expected[i]}\n  actual: {actual[i]}\n");
            }

            mismatches++;
        }

        Harness.AssertEqual("geometry golden mismatches", 0, mismatches);
    }

    private static void BezierLengthTruncated()
    {
        Coord start = Coord.New(0, 0);
        Coord[] control = [Coord.New(5, 10)];
        Coord end = Coord.New(10, 0);
        double length = Geometry.FindLengthOfBezierCurve(start, control, end);

        double reconstructed = 0.0;
        Coord prev = start;
        for (int t = 1; t < 10; t++)
        {
            Coord c = Geometry.FindCoordOnBezierCurve(start, control, end, t / 10.0);
            reconstructed += Geometry.FindLengthOfLine(prev, c, true);
            prev = c;
        }

        Harness.AssertTrue("short length == 1..10 loop", Math.Abs(length - reconstructed) == 0.0);

        Coord atEnd = Geometry.FindCoordOnBezierCurve(start, control, end, 1.0);
        double withFinal = reconstructed + Geometry.FindLengthOfLine(prev, atEnd, true);
        Harness.AssertTrue("omitted t=0.9..1.0 span is positive", withFinal > length);

        // Pin the Python/Rust fixture bits (LE hex of the short length).
        string bits = Fbits(length);
        Harness.AssertTrue(
            "pinned short length within 1e-15 of fixture",
            Math.Abs(length - FromFbits("c0f25d8e1d8a3340")) <= 1e-15
            || bits == "c0f25d8e1d8a3340");
    }

    private static void NormalizedDistanceOob()
    {
        Harness.AssertThrows<ArgumentException>(
            "left of rect",
            () => Geometry.FindNormalizedDistanceFromCenter(1, 8, 1, 10, Coord.New(0, 1)));
        Harness.AssertThrows<ArgumentException>(
            "right of rect",
            () => Geometry.FindNormalizedDistanceFromCenter(1, 8, 1, 10, Coord.New(11, 4)));
        Harness.AssertThrows<ArgumentException>(
            "below rect",
            () => Geometry.FindNormalizedDistanceFromCenter(1, 8, 1, 10, Coord.New(5, 0)));
        Harness.AssertThrows<ArgumentException>(
            "above rect",
            () => Geometry.FindNormalizedDistanceFromCenter(1, 8, 1, 10, Coord.New(5, 9)));
        _ = Geometry.FindNormalizedDistanceFromCenter(1, 8, 1, 10, Coord.New(1, 1));
        _ = Geometry.FindNormalizedDistanceFromCenter(1, 8, 1, 10, Coord.New(10, 8));
        Harness.AssertTrue("corners accepted", true);
    }

    private static void DoubledRowDeltas()
    {
        Coord a = Coord.New(1, 2);
        Coord b = Coord.New(-7, 11);
        double plain = Geometry.FindLengthOfLine(a, b, false);
        double doubled = Geometry.FindLengthOfLine(a, b, true);
        Harness.AssertTrue("doubled row != plain", plain != doubled);
        Harness.AssertTrue(
            "doubled uses 2*row",
            Math.Abs(doubled - double.Hypot(b.Column - a.Column, 2.0 * (b.Row - a.Row))) == 0.0);

        // Bezier length call site passes double_row_diff=true.
        Coord start = Coord.New(0, 0);
        Coord[] control = [Coord.New(5, 10)];
        Coord end = Coord.New(10, 0);
        double bezier = Geometry.FindLengthOfBezierCurve(start, control, end);
        double asPlain = 0.0;
        Coord prev = start;
        for (int t = 1; t < 10; t++)
        {
            Coord c = Geometry.FindCoordOnBezierCurve(start, control, end, t / 10.0);
            asPlain += Geometry.FindLengthOfLine(prev, c, false);
            prev = c;
        }

        Harness.AssertTrue("bezier length uses doubled rows", bezier != asPlain);

        // Circle x-offset is doubled: radius 1 origin (10,10) first point is (12,10) not (11,10).
        List<Coord> circle = Geometry.FindCoordsOnCircle(Coord.New(10, 10), 1, 0, true);
        Harness.AssertTrue("circle x doubled", circle.Count > 0 && circle[0].Column == 12 && circle[0].Row == 10);
    }

    private static void ExpoElasticGuards()
    {
        Harness.AssertTrue("p==0 guard expo", Easing.InExpo.Ease(0.0) == 0.0);
        Harness.AssertTrue("p==1 guard expo", Easing.OutExpo.Ease(1.0) == 1.0);
        Harness.AssertTrue("p==0 elastic", Easing.InElastic.Ease(0.0) == 0.0);
        Harness.AssertTrue("p==1 elastic", Easing.InElastic.Ease(1.0) == 1.0);
    }

    private static byte[] RunPublished(string flag, bool binary)
    {
        string bin = Path.Combine(Harness.FindRepoRoot(), "artifacts", "ttfx");
        if (!File.Exists(bin))
        {
            throw new InvalidOperationException($"AOT binary missing: {bin} (run bin/build)");
        }

        var psi = new ProcessStartInfo
        {
            FileName = bin,
            Arguments = flag,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start artifacts/ttfx");
        using var ms = new MemoryStream();
        proc.StandardOutput.BaseStream.CopyTo(ms);
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"artifacts/ttfx {flag} exited {proc.ExitCode}: {stderr}");
        }

        _ = binary;
        return ms.ToArray();
    }

    private static bool GeometryLineMatches(string expected, string actual, bool linuxGnu)
    {
        if (expected == actual)
        {
            return true;
        }

        if (linuxGnu)
        {
            return false;
        }

        // macOS / non-glibc: coord lines stay exact; float hex lines get 1e-15.
        int expColon = expected.LastIndexOf(": ", StringComparison.Ordinal);
        int actColon = actual.LastIndexOf(": ", StringComparison.Ordinal);
        if (expColon < 0 || actColon < 0)
        {
            return false;
        }

        string expPrefix = expected.Substring(0, expColon);
        string actPrefix = actual.Substring(0, actColon);
        if (expPrefix != actPrefix)
        {
            return false;
        }

        bool floatLine = expected.StartsWith("bezier_len ", StringComparison.Ordinal)
            || expected.StartsWith("line_len ", StringComparison.Ordinal)
            || expected.StartsWith("norm_dist ", StringComparison.Ordinal);
        if (!floatLine)
        {
            return false;
        }

        try
        {
            double exp = FromFbits(expected.Substring(expColon + 2));
            double act = FromFbits(actual.Substring(actColon + 2));
            return Math.Abs(act - exp) <= 1e-15;
        }
        catch (FormatException)
        {
            return false;
        }
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

    private static ulong UlpDiff(double a, double b)
    {
        ulong ba = DoubleToUInt64Bits(a);
        ulong bb = DoubleToUInt64Bits(b);
        return ba >= bb ? ba - bb : bb - ba;
    }

    private static ulong DoubleToUInt64Bits(double x) => BitConverter.DoubleToUInt64Bits(x);

    private static string Fbits(double x)
    {
        byte[] le = BitConverter.GetBytes(x);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(le);
        }

        var sb = new StringBuilder(16);
        foreach (byte b in le)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static double FromFbits(string hex)
    {
        if (hex.Length != 16)
        {
            throw new FormatException(hex);
        }

        byte[] le = Convert.FromHexString(hex);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(le);
        }

        return BitConverter.ToDouble(le, 0);
    }
}
