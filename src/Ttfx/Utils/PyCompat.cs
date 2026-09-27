using System;

namespace Ttfx.Utils;

/// <summary>
/// Helpers reproducing Python semantics where they differ from C# defaults.
/// </summary>
public static class PyCompat
{
    /// <summary>
    /// Float to i64: truncate toward zero.
    /// Not <c>Math.Round</c>, not <c>Convert.ToInt64</c>.
    /// NaN → 0; ±∞ and out-of-range magnitudes saturate.
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
    /// Float to i64 to unsigned size: truncate toward zero, then wrap.
    /// A negative eased value truncates to a negative i64 and wraps to a huge
    /// unsigned value.
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
    /// The floor saturates via <see cref="TruncToI64"/>; the exact-.5
    /// <c>f + 1</c> wraps (unchecked i64 overflow), so +∞ → <c>long.MinValue</c>.
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
    /// IEEE minNum: non-NaN operand; min of signed zeros is −0.
    /// .NET <c>Math.Min</c> propagates NaN.
    /// </summary>
    public static double FMin(double self, double other) => double.MinNumber(self, other);

    /// <summary>
    /// IEEE maxNum: non-NaN operand; max of signed zeros is +0.
    /// .NET <c>Math.Max</c> propagates NaN.
    /// </summary>
    public static double FMax(double self, double other) => double.MaxNumber(self, other);
}
