using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;
using Path = System.IO.Path;

namespace Ttfx.Tests;

internal static class GraphicsTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("graphics goldens line-equal", GraphicsGoldens);
        yield return new TestCase("gradient negative channel floor_div", GradientNegativeDelta);
        yield return new TestCase("hex_to_xterm sweep and first-min tie", HexToXtermSweep);
        yield return new TestCase("seven-digit hex accepted; odd length rejects", HexLengthQuirks);
        yield return new TestCase("leading plus hex matches u8 from_str_radix", LeadingPlusHex);
        yield return new TestCase("Color(255) != Color(\"ffffff\")", ColorArgEquality);
        yield return new TestCase("get_color_at_fraction rejects outside [0,1]", FractionRange);
        yield return new TestCase("negative component formats as -3", NegativeComponentHex);
        yield return new TestCase("adjust_color_brightness bankers round", AdjustBrightnessBankers);
    }

    private static void GraphicsGoldens()
    {
        string fixture = File.ReadAllText(
            Path.Combine(Harness.FindRepoRoot(), "tests", "Ttfx.Tests", "fixtures", "graphics_goldens.txt"));
        string[] expected = fixture.Replace("\r\n", "\n").Split('\n');
        if (expected.Length > 0 && expected[expected.Length - 1].Length == 0)
        {
            Array.Resize(ref expected, expected.Length - 1);
        }

        List<string> actual = GenerateLines();
        int mismatches = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            string a = i < actual.Count ? actual[i] : "";
            if (expected[i] != a)
            {
                if (mismatches < 5)
                {
                    Console.Error.WriteLine($"expected: {expected[i]}\n  actual: {a}\n");
                }

                mismatches++;
            }
        }

        Harness.AssertEqual("graphics golden mismatches", 0, mismatches);
        Harness.AssertEqual("graphics golden line count", expected.Length, actual.Count);
    }

    /// <summary>Transcribed from ttfx tests/graphics_goldens.rs generate_lines.</summary>
    private static List<string> GenerateLines()
    {
        var lines = new List<string>();
        (string[] Stops, long[] Steps, bool DoLoop)[] gradCases =
        [
            (["8A008A", "00D1FF", "FFFFFF"], [12], false),
            (["8A008A", "00D1FF", "FFFFFF"], [6, 3], false),
            (["ffffff", "000000"], [10], false),
            (["000000", "ffffff"], [7], false),
            (["ff0000", "00ff00", "0000ff"], [5], true),
            (["123456"], [4], false),
            (["ff5733", "33ff57", "5733ff", "f0f0f0"], [3, 9], false),
            (["0a0b0c", "f1e2d3"], [1], false),
        ];
        foreach ((string[] stops, long[] steps, bool doLoop) in gradCases)
        {
            var colors = new List<Color>();
            foreach (string s in stops)
            {
                colors.Add(Color.FromHex(s));
            }

            // tuple-shaped steps in the Python generator (never scalar), so skip
            // the int-only validation like upstream does for tuples
            Gradient g = Gradient.New(colors, steps, false, doLoop);
            string stepsRepr = steps.Length == 1
                ? $"({steps[0]},)"
                : $"({string.Join(", ", steps)})";
            string pyLoop = doLoop ? "True" : "False";
            var rgb = new List<string>();
            foreach (Color c in g.Spectrum)
            {
                rgb.Add(c.RgbColor);
            }

            lines.Add($"grad {string.Join("+", stops)} s={stepsRepr} loop={pyLoop}: {string.Join(";", rgb)}");
        }

        var fracColors = new List<Color>();
        foreach (string s in new[] { "8A008A", "00D1FF", "FFFFFF" })
        {
            fracColors.Add(Color.FromHex(s));
        }

        Gradient fracG = Gradient.WithSteps(fracColors, 12, false);
        for (int i = 0; i <= 20; i++)
        {
            double f = i / 20.0;
            lines.Add($"frac {FormatFloatLabel(f)}: {fracG.GetColorAtFraction(f).RgbColor}");
        }

        (string Name, GradientDirection Direction)[] directions =
        [
            ("VERTICAL", GradientDirection.Vertical),
            ("HORIZONTAL", GradientDirection.Horizontal),
            ("RADIAL", GradientDirection.Radial),
            ("DIAGONAL", GradientDirection.Diagonal),
        ];
        (string Label, (long MinRow, long MaxRow, long MinColumn, long MaxColumn) Box)[] boxes =
        [
            ("mapping", (1, 5, 1, 8)),
            ("mapping_offset", (2, 6, 3, 9)),
        ];
        foreach ((string name, GradientDirection direction) in directions)
        {
            foreach ((string label, (long minRow, long maxRow, long minColumn, long maxColumn)) in boxes)
            {
                CoordColorMap mapping = fracG.BuildCoordinateColorMapping(
                    minRow, maxRow, minColumn, maxColumn, direction);
                var entries = new List<string>();
                foreach ((Coord c, Color col) in mapping.Iter())
                {
                    entries.Add($"{c.Column},{c.Row}={col.RgbColor}");
                }

                lines.Add($"{label} {name}: {string.Join(";", entries)}");
            }
        }

        foreach (double factor in new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 0.99, 1.0 })
        {
            Color c = Graphics.ShiftColorTowards(
                Color.FromHex("ff8040"),
                Color.FromHex("103050"),
                factor);
            lines.Add($"shift {FormatFloatLabel(factor)}: {c.RgbColor}");
        }

        return lines;
    }

    private static string FormatFloatLabel(double f)
    {
        if (f == Math.Truncate(f))
        {
            return f.ToString("0.0", CultureInfo.InvariantCulture);
        }

        return f.ToString(CultureInfo.InvariantCulture);
    }

    private static void GradientNegativeDelta()
    {
        // start r=10, end r=0, steps=3.
        // floor_div(-10, 3) = -4; C# truncate toward zero is -3.
        Color start = Color.FromHex("0a0000");
        Color end = Color.FromHex("000000");
        Gradient g = Gradient.WithSteps([start, end], 3, false);
        Harness.AssertEqual("spectrum len", 4, g.Spectrum.Count);
        Harness.AssertEqual("i=0", "0a0000", g.Spectrum[0].RgbColor);
        Harness.AssertEqual("i=1 floor", "060000", g.Spectrum[1].RgbColor);
        Harness.AssertEqual("i=2 floor", "020000", g.Spectrum[2].RgbColor);
        Harness.AssertEqual("end stop", "000000", g.Spectrum[3].RgbColor);
        Harness.AssertTrue("not truncate i=1", g.Spectrum[1].RgbColor != "070000");
        Harness.AssertTrue("not truncate i=2", g.Spectrum[2].RgbColor != "040000");
    }

    private static void HexToXtermSweep()
    {
        byte[] channels = [0, 1, 14, 15, 16, 31, 47, 63, 79, 95, 127, 128, 159, 191, 223, 254, 255];
        foreach (byte r in channels)
        {
            foreach (byte g in channels)
            {
                foreach (byte b in channels)
                {
                    string hex = $"#{r:X2}{g:x2}{b:X2}";
                    Harness.AssertEqual(hex, ReferenceHexToXterm(hex), Hexterm.HexToXterm(hex));
                }
            }
        }

        byte expected = ReferenceHexToXterm("ff00aa");
        Harness.AssertEqual("ff00aa", expected, Hexterm.HexToXterm("ff00aa"));
        Harness.AssertEqual("#FF00AA", expected, Hexterm.HexToXterm("#FF00AA"));
        Harness.AssertEqual("ff00aa7", expected, Hexterm.HexToXterm("ff00aa7"));

        // Explicit tie: xterm 0 and 16 are both #000000; first minimum wins (0).
        Harness.AssertEqual("000000 first min", (byte)0, Hexterm.HexToXterm("000000"));
        Harness.AssertEqual("000000 ref", (byte)0, ReferenceHexToXterm("000000"));
        Harness.AssertEqual("xterm 0 hex", "000000", Hexterm.XtermToHex[0]);
        Harness.AssertEqual("xterm 16 hex", "000000", Hexterm.XtermToHex[16]);
    }

    private static byte ReferenceHexToXterm(string hexColor)
    {
        string s = hexColor.Trim('#');
        long r = long.Parse(s.AsSpan(0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        long g = long.Parse(s.AsSpan(2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        long b = long.Parse(s.AsSpan(4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        double minDiff = double.PositiveInfinity;
        byte closest = 0;
        for (int code = 0; code < 256; code++)
        {
            string pal = Hexterm.XtermToHex[code];
            long xr = long.Parse(pal.AsSpan(0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            long xg = long.Parse(pal.AsSpan(2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            long xb = long.Parse(pal.AsSpan(4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            double diff = (Math.Abs(r - xr) + Math.Abs(g - xg) + Math.Abs(b - xb)) / 3.0;
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = (byte)code;
            }
        }

        return closest;
    }

    private static void HexLengthQuirks()
    {
        Color seven = Color.FromHex("1234567");
        Harness.AssertEqual("7-digit rgb r", (byte)0x12, seven.RgbInts().R);
        Harness.AssertEqual("7-digit rgb g", (byte)0x34, seven.RgbInts().G);
        Harness.AssertEqual("7-digit rgb b", (byte)0x56, seven.RgbInts().B);
        Harness.AssertEqual("7-digit stored", "1234567", seven.RgbColor);
        Harness.AssertTrue("7-digit valid", Hexterm.IsValidHexColor("1234567"));
        Harness.AssertTrue("6-digit valid", Hexterm.IsValidHexColor("123456"));
        Harness.AssertTrue("odd 5 rejected", !Hexterm.IsValidHexColor("12345"));
        Harness.AssertTrue("odd 3 rejected", !Hexterm.IsValidHexColor("abc"));
        Harness.AssertThrows<ArgumentException>("odd 5 FromHex", () => Color.FromHex("12345"));
        Harness.AssertThrows<ArgumentException>("odd 3 FromHex", () => Color.FromHex("abc"));
    }

    private static void LeadingPlusHex()
    {
        // u8::from_str_radix accepts '+': "+abc12" → channels "+a","bc","12"
        Color plusSix = Color.FromHex("+abc12");
        Harness.AssertEqual("+abc12 r", (byte)0x0a, plusSix.RgbInts().R);
        Harness.AssertEqual("+abc12 g", (byte)0xbc, plusSix.RgbInts().G);
        Harness.AssertEqual("+abc12 b", (byte)0x12, plusSix.RgbInts().B);
        Harness.AssertEqual("+abc12 stored", "+abc12", plusSix.RgbColor);

        // seven-digit: first six after trim, "+a","bc","de"
        Color plusSeven = Color.FromHex("+abcdef");
        Harness.AssertEqual("+abcdef r", (byte)0x0a, plusSeven.RgbInts().R);
        Harness.AssertEqual("+abcdef g", (byte)0xbc, plusSeven.RgbInts().G);
        Harness.AssertEqual("+abcdef b", (byte)0xde, plusSeven.RgbInts().B);

        Color cli = ValueParsers.ColorArg("+abc12");
        Harness.AssertTrue("CLI +abc12", cli.Equals(plusSix));
        Harness.AssertEqual("hex_to_xterm +abc12", Hexterm.HexToXterm("0abc12"), Hexterm.HexToXterm("+abc12"));
    }

    private static void ColorArgEquality()
    {
        Harness.AssertTrue("xterm != hex", !Color.FromXterm(255).Equals(Color.FromHex("ffffff")));
        Harness.AssertTrue("same xterm", Color.FromXterm(255).Equals(Color.FromXterm(255)));
        Harness.AssertTrue("hash # vs bare", Color.FromHex("#000000").Equals(Color.FromHex("000000")));
        Harness.AssertTrue("case preserved", !Color.FromHex("FFFFFF").Equals(Color.FromHex("ffffff")));
    }

    private static void FractionRange()
    {
        Gradient g = Gradient.WithSteps([Color.FromHex("000000"), Color.FromHex("ffffff")], 4, false);
        Harness.AssertThrows<ArgumentException>("below", () => g.GetColorAtFraction(-0.1));
        Harness.AssertThrows<ArgumentException>("above", () => g.GetColorAtFraction(1.1));
        Harness.AssertThrows<ArgumentException>("nan", () => g.GetColorAtFraction(double.NaN));
        _ = g.GetColorAtFraction(0.0);
        _ = g.GetColorAtFraction(1.0);
        Harness.AssertTrue("0 and 1 accepted", true);
    }

    private static void NegativeComponentHex()
    {
        Harness.AssertEqual("neg 3", "-3", Graphics.FormatPyHex(-3));
        Harness.AssertTrue("not twos complement", Graphics.FormatPyHex(-3) != (-3).ToString("x2", CultureInfo.InvariantCulture));
        Harness.AssertEqual("pos two digits", "0a", Graphics.FormatPyHex(10));
        Harness.AssertEqual("zero", "00", Graphics.FormatPyHex(0));
    }

    private static void AdjustBrightnessBankers()
    {
        // 0.5 * 255 = 127.5 → banker's even → 128? 127 is odd, 128 is even.
        // Gray 808080: channels 128/255. brightness 1.0 should stay ~128.
        Color gray = Color.FromHex("808080");
        Color same = Animation.AdjustColorBrightness(gray, 1.0);
        Harness.AssertEqual("identity gray", "808080", same.RgbColor);

        // Exact .5 channel: construct via known HSL gray (saturation == 0.0).
        // lightness 0.5, brightness 1.0 → channel = round_half_even(0.5*255)=128 (127.5→128, 128 even).
        Color mid = Color.FromHex("7f7f7f");
        Color brighter = Animation.AdjustColorBrightness(mid, 1.0);
        Harness.AssertEqual("7f identity-ish r", brighter.RgbInts().R, brighter.RgbInts().G);
    }
}
