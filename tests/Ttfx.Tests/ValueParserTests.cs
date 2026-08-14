using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class ValueParserTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("color xterm vs hex equality", ColorEquality);
        yield return new TestCase("color reject grammar", ColorReject);
        yield return new TestCase("symbol one rune", SymbolRune);
        yield return new TestCase("float helpers", FloatHelpers);
        yield return new TestCase("positive int whitespace", PositiveIntWhitespace);
    }

    private static void ColorEquality()
    {
        Color a = ValueParsers.ColorArg("255");
        Color b = ValueParsers.ColorArg("ffffff");
        Harness.AssertTrue("255 != ffffff", !a.Equals(b));
        Harness.AssertTrue("255 == 255", a.Equals(ValueParsers.ColorArg("255")));
        Color hex = ValueParsers.ColorArg("#000000");
        Harness.AssertEqual("keep token", "#000000", hex.Original);
        Color seven = ValueParsers.ColorArg("12AbEf7");
        Harness.AssertEqual("7-digit hex", "12AbEf7", seven.Original);

        Color fromXterm = Color.FromXterm(255);
        Harness.AssertTrue("255 is Xterm", a.ColorArg is ColorArg.Xterm);
        Harness.AssertTrue("255 ColorArg == FromXterm", a.ColorArg.Equals(fromXterm.ColorArg));
        Harness.AssertEqual("255 rgb r", fromXterm.RgbInts().R, a.RgbInts().R);
        Harness.AssertEqual("255 rgb g", fromXterm.RgbInts().G, a.RgbInts().G);
        Harness.AssertEqual("255 rgb b", fromXterm.RgbInts().B, a.RgbInts().B);
        Harness.AssertTrue("255 equals FromXterm (same token)", a.Equals(fromXterm));

        Color fromHex = Color.FromHex("ffffff");
        Harness.AssertTrue("ffffff is Hex", b.ColorArg is ColorArg.Hex);
        Harness.AssertTrue("ffffff ColorArg == FromHex", b.ColorArg.Equals(fromHex.ColorArg));
        Harness.AssertEqual("ffffff rgb r", fromHex.RgbInts().R, b.RgbInts().R);
        Harness.AssertEqual("ffffff rgb g", fromHex.RgbInts().G, b.RgbInts().G);
        Harness.AssertEqual("ffffff rgb b", fromHex.RgbInts().B, b.RgbInts().B);
        Harness.AssertTrue("ffffff equals FromHex (same token)", b.Equals(fromHex));

        Color hashed = Color.FromHex("#000000");
        Color bare = Color.FromHex("000000");
        Harness.AssertTrue("#000000 == 000000 (Hex stores stripped rgb_color)", hashed.Equals(bare));
        Harness.AssertTrue("Color(255) != Color(\"ffffff\")", !Color.FromXterm(255).Equals(Color.FromHex("ffffff")));
    }

    private static void ColorReject()
    {
        Harness.AssertThrows<UsageError>("xyz", () => ValueParsers.ColorArg("xyz"));
        Harness.AssertThrows<UsageError>("256", () => ValueParsers.ColorArg("256"));
        Harness.AssertThrows<UsageError>("ffff", () => ValueParsers.ColorArg("ffff"));
        Harness.AssertThrows<UsageError>("8 hex", () => ValueParsers.ColorArg("aaaaaaaa"));
        Harness.AssertThrows<UsageError>("padded xterm", () => ValueParsers.ColorArg(" 1"));
    }

    private static void SymbolRune()
    {
        Harness.AssertEqual("ascii", "-", ValueParsers.Symbol("-"));
        Harness.AssertEqual("equals", "=", ValueParsers.Symbol("="));
        Harness.AssertEqual("dot", ".", ValueParsers.Symbol("."));
        Harness.AssertEqual("block", "▂", ValueParsers.Symbol("▂"));
        Harness.AssertThrows<UsageError>("empty", () => ValueParsers.Symbol(""));
        Harness.AssertThrows<UsageError>("two", () => ValueParsers.Symbol("ab"));
        Harness.AssertEqual("emoji", "😀", ValueParsers.Symbol("😀"));
    }

    private static void FloatHelpers()
    {
        Harness.AssertEqual("pos", 1.5, ValueParsers.PositiveFloat("1.5"));
        Harness.AssertThrows<UsageError>("pos 0", () => ValueParsers.PositiveFloat("0"));
        Harness.AssertEqual("nn", 0.0, ValueParsers.NonNegativeFloat("0"));
        Harness.AssertThrows<UsageError>("nn -1", () => ValueParsers.NonNegativeFloat("-1"));
        Harness.AssertEqual("nnr", 1.0, ValueParsers.NonNegativeRatio("1"));
        Harness.AssertThrows<UsageError>("nnr 1.1", () => ValueParsers.NonNegativeRatio("1.1"));
        Harness.AssertEqual("pr", 0.5, ValueParsers.PositiveRatio("0.5"));
        Harness.AssertThrows<UsageError>("pr 0", () => ValueParsers.PositiveRatio("0"));
        Harness.AssertThrows<UsageError>("ws float", () => ValueParsers.PositiveFloat(" 1.0 "));
    }

    private static void PositiveIntWhitespace()
    {
        Harness.AssertThrows<UsageError>("padded", () => ValueParsers.PositiveInt(" 4 "));
        Harness.AssertEqual("+4", 4L, ValueParsers.PositiveInt("+4"));
    }
}
