using System;
using System.Collections.Generic;
using Ttfx.Utils;

namespace Ttfx.Tests;

internal static class PyCompatTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("TruncToI64 toward zero and saturation", TruncToI64);
        yield return new TestCase("TruncToUsize two-step wrap", TruncToUsize);
        yield return new TestCase("FloorDiv floors negatives", FloorDiv);
        yield return new TestCase("PyMod sign-of-divisor", PyMod);
        yield return new TestCase("RoundHalfEven pinned vs Rust", RoundHalfEven);
        yield return new TestCase("FMin/FMax NaN and signed zero", FMinFMax);
    }

    private static void AssertBits(string name, double expected, double actual)
    {
        Harness.AssertEqual(
            name,
            BitConverter.DoubleToUInt64Bits(expected),
            BitConverter.DoubleToUInt64Bits(actual));
    }

    private static void TruncToI64()
    {
        Harness.AssertEqual("trunc 1.9", 1L, PyCompat.TruncToI64(1.9));
        Harness.AssertEqual("trunc -1.9", -1L, PyCompat.TruncToI64(-1.9));
        Harness.AssertEqual("trunc 0.1", 0L, PyCompat.TruncToI64(0.1));
        Harness.AssertEqual("trunc -0.1", 0L, PyCompat.TruncToI64(-0.1));
        Harness.AssertEqual("trunc 0.0", 0L, PyCompat.TruncToI64(0.0));
        Harness.AssertEqual("trunc -0.0", 0L, PyCompat.TruncToI64(-0.0));
        Harness.AssertEqual("trunc NaN", 0L, PyCompat.TruncToI64(double.NaN));
        Harness.AssertEqual("trunc +inf", long.MaxValue, PyCompat.TruncToI64(double.PositiveInfinity));
        Harness.AssertEqual("trunc -inf", long.MinValue, PyCompat.TruncToI64(double.NegativeInfinity));
        Harness.AssertEqual("trunc 1e20", long.MaxValue, PyCompat.TruncToI64(1e20));
        Harness.AssertEqual("trunc -1e20", long.MinValue, PyCompat.TruncToI64(-1e20));
        Harness.AssertEqual("trunc 2^63", long.MaxValue, PyCompat.TruncToI64((double)long.MaxValue + 1.0));
        Harness.AssertEqual("trunc i64.Min", long.MinValue, PyCompat.TruncToI64(long.MinValue));
    }

    private static void TruncToUsize()
    {
        Harness.AssertEqual("usize 2.9", (nuint)2, PyCompat.TruncToUsize(2.9));
        Harness.AssertEqual("usize 0.1", (nuint)0, PyCompat.TruncToUsize(0.1));
        Harness.AssertEqual("usize -0.1 toward zero", (nuint)0, PyCompat.TruncToUsize(-0.1));
        Harness.AssertEqual("usize -1.0 wrap", nuint.MaxValue, PyCompat.TruncToUsize(-1.0));
        Harness.AssertEqual("usize -1.5 wrap", nuint.MaxValue, PyCompat.TruncToUsize(-1.5));
        Harness.AssertEqual("usize -1.9 wrap", nuint.MaxValue, PyCompat.TruncToUsize(-1.9));
        Harness.AssertEqual("usize -2.5 wrap", nuint.MaxValue - 1, PyCompat.TruncToUsize(-2.5));
        Harness.AssertEqual("usize -inf wrap", unchecked((nuint)(ulong)long.MinValue), PyCompat.TruncToUsize(double.NegativeInfinity));
        Harness.AssertEqual("usize +inf", unchecked((nuint)long.MaxValue), PyCompat.TruncToUsize(double.PositiveInfinity));
        Harness.AssertEqual("usize NaN", (nuint)0, PyCompat.TruncToUsize(double.NaN));
    }

    private static void FloorDiv()
    {
        Harness.AssertEqual("7//2", 3L, PyCompat.FloorDiv(7, 2));
        Harness.AssertEqual("-7//2", -4L, PyCompat.FloorDiv(-7, 2));
        Harness.AssertEqual("7//-2", -4L, PyCompat.FloorDiv(7, -2));
        Harness.AssertEqual("-7//-2", 3L, PyCompat.FloorDiv(-7, -2));
        Harness.AssertEqual("7//2 exact vs C# /", 3L, 7L / 2L);
        Harness.AssertEqual("-7/2 truncates", -3L, -7L / 2L);
        Harness.AssertEqual("0//-2", 0L, PyCompat.FloorDiv(0, -2));
        Harness.AssertEqual("-1//1", -1L, PyCompat.FloorDiv(-1, 1));
    }

    private static void PyMod()
    {
        Harness.AssertEqual("7%2", 1L, PyCompat.PyMod(7, 2));
        Harness.AssertEqual("-7%2", 1L, PyCompat.PyMod(-7, 2));
        Harness.AssertEqual("7%-2", -1L, PyCompat.PyMod(7, -2));
        Harness.AssertEqual("-7%-2", -1L, PyCompat.PyMod(-7, -2));
        Harness.AssertEqual("0%-2", 0L, PyCompat.PyMod(0, -2));
        Harness.AssertEqual("C# -7%2 is -1", -1L, -7L % 2L);
    }

    private static void RoundHalfEven()
    {
        Harness.AssertEqual("round 0.5", 0L, PyCompat.RoundHalfEven(0.5));
        Harness.AssertEqual("round 1.5", 2L, PyCompat.RoundHalfEven(1.5));
        Harness.AssertEqual("round 2.5", 2L, PyCompat.RoundHalfEven(2.5));
        Harness.AssertEqual("round 3.5", 4L, PyCompat.RoundHalfEven(3.5));
        Harness.AssertEqual("round -0.5", 0L, PyCompat.RoundHalfEven(-0.5));
        Harness.AssertEqual("round -1.5", -2L, PyCompat.RoundHalfEven(-1.5));
        Harness.AssertEqual("round -2.5", -2L, PyCompat.RoundHalfEven(-2.5));
        Harness.AssertEqual("round 0.4999", 0L, PyCompat.RoundHalfEven(0.4999));
        Harness.AssertEqual("round 1.4999", 1L, PyCompat.RoundHalfEven(1.4999));
        Harness.AssertEqual("round 2.6", 3L, PyCompat.RoundHalfEven(2.6));
        Harness.AssertEqual("round -2.6", -3L, PyCompat.RoundHalfEven(-2.6));
        Harness.AssertEqual("round 2.675", 3L, PyCompat.RoundHalfEven(2.675));
        Harness.AssertEqual("round +0", 0L, PyCompat.RoundHalfEven(0.0));
        Harness.AssertEqual("round -0", 0L, PyCompat.RoundHalfEven(-0.0));
        Harness.AssertEqual("round NaN", 0L, PyCompat.RoundHalfEven(double.NaN));
        Harness.AssertEqual("round +inf wraps", long.MinValue, PyCompat.RoundHalfEven(double.PositiveInfinity));
        Harness.AssertEqual("round -inf", long.MinValue, PyCompat.RoundHalfEven(double.NegativeInfinity));
        Harness.AssertEqual("round 1e20 saturates", long.MaxValue, PyCompat.RoundHalfEven(1e20));
        Harness.AssertEqual("round -1e20 saturates", long.MinValue, PyCompat.RoundHalfEven(-1e20));
        Harness.AssertEqual("round 1.9", 2L, PyCompat.RoundHalfEven(1.9));
        Harness.AssertEqual("round -1.9", -2L, PyCompat.RoundHalfEven(-1.9));
        Harness.AssertEqual("round 1.0", 1L, PyCompat.RoundHalfEven(1.0));
        Harness.AssertEqual("round -1.0", -1L, PyCompat.RoundHalfEven(-1.0));
    }

    private static void FMinFMax()
    {
        AssertBits("min(-0, +0)", -0.0, PyCompat.FMin(-0.0, 0.0));
        AssertBits("min(+0, -0)", -0.0, PyCompat.FMin(0.0, -0.0));
        AssertBits("max(-0, +0)", 0.0, PyCompat.FMax(-0.0, 0.0));
        AssertBits("max(+0, -0)", 0.0, PyCompat.FMax(0.0, -0.0));
        AssertBits("min(-0, -0)", -0.0, PyCompat.FMin(-0.0, -0.0));
        AssertBits("max(+0, +0)", 0.0, PyCompat.FMax(0.0, 0.0));

        Harness.AssertEqual("min(NaN, 1)", 1.0, PyCompat.FMin(double.NaN, 1.0));
        Harness.AssertEqual("min(1, NaN)", 1.0, PyCompat.FMin(1.0, double.NaN));
        Harness.AssertEqual("max(NaN, 1)", 1.0, PyCompat.FMax(double.NaN, 1.0));
        Harness.AssertEqual("max(1, NaN)", 1.0, PyCompat.FMax(1.0, double.NaN));
        Harness.AssertTrue("min(NaN, NaN)", double.IsNaN(PyCompat.FMin(double.NaN, double.NaN)));
        Harness.AssertTrue("max(NaN, NaN)", double.IsNaN(PyCompat.FMax(double.NaN, double.NaN)));

        Harness.AssertEqual("min(+inf, 1)", 1.0, PyCompat.FMin(double.PositiveInfinity, 1.0));
        Harness.AssertEqual("max(-inf, 1)", 1.0, PyCompat.FMax(double.NegativeInfinity, 1.0));
        Harness.AssertEqual("min(-inf, 1)", double.NegativeInfinity, PyCompat.FMin(double.NegativeInfinity, 1.0));
        Harness.AssertEqual("max(+inf, 1)", double.PositiveInfinity, PyCompat.FMax(double.PositiveInfinity, 1.0));

        Harness.AssertTrue("Math.Min propagates NaN", double.IsNaN(Math.Min(double.NaN, 1.0)));
        Harness.AssertTrue("Math.Max propagates NaN", double.IsNaN(Math.Max(1.0, double.NaN)));
    }
}
