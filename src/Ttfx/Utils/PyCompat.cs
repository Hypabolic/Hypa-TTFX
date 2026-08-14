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
}
