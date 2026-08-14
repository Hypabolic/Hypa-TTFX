using System;

namespace Ttfx.Utils;

/// <summary>
/// Helpers reproducing Python semantics where they differ from C# defaults.
/// Transcribed from <c>utils/pycompat.rs</c>.
/// </summary>
public static class PyCompat
{
    /// <summary>
    /// Rust <c>as i64</c> on a float: truncate toward zero.
    /// Not <c>Math.Round</c>, not <c>Convert.ToInt64</c>.
    /// NaN → 0; ±∞ and out-of-range magnitudes saturate, matching Rust.
    /// </summary>
    public static long TruncToI64(double x)
    {
        if (double.IsNaN(x))
        {
            return 0;
        }

        if (x >= long.MaxValue)
        {
            return long.MaxValue;
        }

        if (x <= long.MinValue)
        {
            return long.MinValue;
        }

        return (long)x;
    }

    /// <summary>
    /// Rust <c>as i64 as usize</c>: truncate toward zero, then wrap.
    /// <c>easing.rs:356-357</c> — a negative eased value truncates to a
    /// negative <c>i64</c> and wraps to a huge <c>usize</c>.
    /// </summary>
    public static nuint TruncToUsize(double x)
    {
        long truncated = TruncToI64(x);
        return unchecked((nuint)(ulong)truncated);
    }

    /// <summary>
    /// Python's <c>//</c> on integers: floor division.
    /// C# <c>/</c> truncates toward zero.
    /// </summary>
    public static long FloorDiv(long a, long b)
    {
        long q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0))
        {
            return q - 1;
        }

        return q;
    }

    /// <summary>
    /// Python's <c>%</c> on integers: result takes the sign of the divisor.
    /// Transcribed from <c>utils/pycompat.rs</c> <c>py_mod</c>.
    /// </summary>
    public static long PyMod(long a, long b)
    {
        long r = a % b;
        if (r != 0 && (r < 0) != (b < 0))
        {
            return r + b;
        }

        return r;
    }

    /// <summary>
    /// Python's built-in <c>round()</c>: banker's rounding (half-to-even), returning i64.
    /// Transcribed from <c>utils/pycompat.rs</c>.
    /// <c>floor as i64</c> saturates via <see cref="TruncToI64"/>; the exact-.5
    /// <c>f + 1</c> wraps (Rust release <c>i64</c> overflow), so +∞ → <c>i64::MIN</c>.
    /// </summary>
    public static long RoundHalfEven(double x)
    {
        double floor = Math.Floor(x);
        double diff = x - floor;
        if (diff > 0.5)
        {
            return unchecked(TruncToI64(floor) + 1);
        }

        if (diff < 0.5)
        {
            return TruncToI64(floor);
        }

        // exactly .5 — round to even
        long f = TruncToI64(floor);
        if (f % 2 == 0)
        {
            return f;
        }

        return unchecked(f + 1);
    }

    /// <summary>
    /// Rust <c>f64::min</c> (IEEE minNum): non-NaN operand; min of signed zeros is −0.
    /// .NET <c>Math.Min</c> propagates NaN.
    /// </summary>
    public static double FMin(double self, double other) => double.MinNumber(self, other);

    /// <summary>
    /// Rust <c>f64::max</c> (IEEE maxNum): non-NaN operand; max of signed zeros is +0.
    /// .NET <c>Math.Max</c> propagates NaN.
    /// </summary>
    public static double FMax(double self, double other) => double.MaxNumber(self, other);
}
