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
    /// Python's built-in <c>round()</c>: banker's rounding (half-to-even), returning i64.
    /// Transcribed from <c>utils/pycompat.rs</c>.
    /// </summary>
    public static long RoundHalfEven(double x)
    {
        double floor = System.Math.Floor(x);
        double diff = x - floor;
        if (diff > 0.5)
        {
            return (long)floor + 1;
        }

        if (diff < 0.5)
        {
            return (long)floor;
        }

        // exactly .5 — round to even
        long f = (long)floor;
        if (f % 2 == 0)
        {
            return f;
        }

        return f + 1;
    }

    /// <summary>
    /// Rust <c>f64::min</c>: returns the non-NaN operand; .NET <c>Math.Min</c> propagates NaN.
    /// </summary>
    public static double FMin(double self, double other)
    {
        if (other < self || double.IsNaN(self))
        {
            return other;
        }

        return self;
    }

    /// <summary>
    /// Rust <c>f64::max</c>: returns the non-NaN operand; .NET <c>Math.Max</c> propagates NaN.
    /// </summary>
    public static double FMax(double self, double other)
    {
        if (self < other || double.IsNaN(self))
        {
            return other;
        }

        return self;
    }
}
