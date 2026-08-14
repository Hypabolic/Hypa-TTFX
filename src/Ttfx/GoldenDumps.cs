using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ttfx.Utils;

namespace Ttfx;

/// <summary>
/// Hidden-flag dumps of the easing and geometry goldens. Emitted by the
/// AOT-published binary so assertions run under ILC, not RyuJIT.
/// Line formats match <c>tests/easing_goldens.rs</c> / <c>geometry_goldens.rs</c>.
/// </summary>
internal static class GoldenDumps
{
    internal static readonly Easing[] EasingGoldenOrder =
    [
        Easing.Linear,
        Easing.InSine,
        Easing.OutSine,
        Easing.InOutSine,
        Easing.InQuad,
        Easing.OutQuad,
        Easing.InOutQuad,
        Easing.InCubic,
        Easing.OutCubic,
        Easing.InOutCubic,
        Easing.InQuart,
        Easing.OutQuart,
        Easing.InOutQuart,
        Easing.InQuint,
        Easing.OutQuint,
        Easing.InOutQuint,
        Easing.InExpo,
        Easing.OutExpo,
        Easing.InOutExpo,
        Easing.InCirc,
        Easing.OutCirc,
        Easing.InOutCirc,
        Easing.InBack,
        Easing.OutBack,
        Easing.InOutBack,
        Easing.InElastic,
        Easing.OutElastic,
        Easing.InOutElastic,
        Easing.InBounce,
        Easing.OutBounce,
        Easing.InOutBounce,
        Easing.MakeEasing(0.25, 0.1, 0.25, 1.0),
        Easing.MakeEasing(0.42, 0.0, 0.58, 1.0),
        Easing.MakeEasing(0.68, -0.55, 0.265, 1.55),
    ];

    internal static int WriteEasing(Stream stdout)
    {
        foreach (Easing easing in EasingGoldenOrder)
        {
            for (int i = 0; i <= 1000; i++)
            {
                double p = i / 1000.0;
                double actual = easing.Ease(p);
                byte[] le = BitConverter.GetBytes(actual);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(le);
                }

                stdout.Write(le);
            }
        }

        stdout.Flush();
        return 0;
    }

    internal static int WriteGeometry(TextWriter w)
    {
        foreach (string line in GenerateGeometryLines())
        {
            w.WriteLine(line);
        }

        w.Flush();
        return 0;
    }

    /// <summary>Transcribed from ttfx <c>tests/geometry_goldens.rs</c> generate_lines.</summary>
    internal static List<string> GenerateGeometryLines()
    {
        var lines = new List<string>();

        foreach (long radius in (long[])[1, 2, 3, 5, 8, 13, 20])
        {
            foreach (long limit in (long[])[0, 7, 100])
            {
                foreach (bool unique in (bool[])[true, false])
                {
                    List<Coord> got = Geometry.FindCoordsOnCircle(Coord.New(10, 10), radius, limit, unique);
                    string u = unique ? "True" : "False";
                    lines.Add($"on_circle r={radius} l={limit} u={u}: {Coords(got)}");
                }
            }
        }

        foreach (long diameter in (long[])[1, 2, 3, 4, 7, 10, 15])
        {
            List<Coord> got = Geometry.FindCoordsInCircle(Coord.New(5, -3), diameter);
            lines.Add($"in_circle d={diameter}: {Coords(got)}");
        }

        foreach (long distance in (long[])[0, 1, 2, 5])
        {
            lines.Add($"in_rect d={distance}: {Coords(Geometry.FindCoordsInRect(Coord.New(3, 4), distance))}");
        }

        foreach ((long hw, long hh) in new (long, long)[] { (0, 3), (3, 0), (1, 1), (4, 2), (5, 7) })
        {
            lines.Add($"on_rect {hw},{hh}: {Coords(Geometry.FindCoordsOnRect(Coord.New(0, 0), hw, hh))}");
        }

        foreach ((Coord origin, Coord target) in new (Coord, Coord)[]
        {
            (Coord.New(0, 0), Coord.New(10, 5)),
            (Coord.New(3, 3), Coord.New(3, 3)),
            (Coord.New(-5, 2), Coord.New(7, -9)),
        })
        {
            foreach (double offset in (double[])[0.0, 1.5, 4.0, 10.25, -2.0])
            {
                Coord c = Geometry.ExtrapolateAlongRay(origin, target, offset);
                lines.Add(
                    $"extrapolate {origin.Column},{origin.Row}->{target.Column},{target.Row}+{RustF64Debug(offset)}: {c.Column},{c.Row}");
            }
        }

        (Coord Start, Coord[] Control, Coord End)[] bezierCases =
        [
            (Coord.New(0, 0), [Coord.New(5, 10)], Coord.New(10, 0)),
            (Coord.New(0, 0), [Coord.New(3, 8), Coord.New(7, -2)], Coord.New(12, 4)),
            (Coord.New(-4, -4), [Coord.New(0, 20), Coord.New(9, 9), Coord.New(-3, 2)], Coord.New(6, -6)),
        ];
        foreach ((Coord start, Coord[] control, Coord end) in bezierCases)
        {
            var pts = new List<Coord>();
            for (int i = 0; i <= 20; i++)
            {
                double t = i / 20.0;
                pts.Add(Geometry.FindCoordOnBezierCurve(start, control, end, t));
            }

            lines.Add($"bezier {control.Length}cp: {Coords(pts)}");
            lines.Add($"bezier_len {control.Length}cp: {Fbits(Geometry.FindLengthOfBezierCurve(start, control, end))}");
        }

        var linePts = new List<Coord>();
        for (int i = -5; i <= 25; i++)
        {
            double t = i / 20.0;
            linePts.Add(Geometry.FindCoordOnLine(Coord.New(-3, 7), Coord.New(14, -2), t));
        }

        lines.Add($"on_line: {Coords(linePts)}");

        foreach (bool doubled in (bool[])[false, true])
        {
            double v = Geometry.FindLengthOfLine(Coord.New(1, 2), Coord.New(-7, 11), doubled);
            string d = doubled ? "True" : "False";
            lines.Add($"line_len double={d}: {Fbits(v)}");
        }

        foreach (Coord coord in (Coord[])[Coord.New(1, 1), Coord.New(5, 3), Coord.New(10, 8), Coord.New(3, 8), Coord.New(10, 1)])
        {
            double v = Geometry.FindNormalizedDistanceFromCenter(1, 8, 1, 10, coord);
            lines.Add($"norm_dist {coord.Column},{coord.Row}: {Fbits(v)}");
        }

        return lines;
    }

    internal static string Fbits(double x)
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

    private static string Coords(IReadOnlyList<Coord> cs)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cs.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            sb.Append(cs[i].Column.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(cs[i].Row.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Rust <c>{offset:?}</c> for the golden offsets: always a decimal point.</summary>
    private static string RustF64Debug(double x)
    {
        if (x == Math.Truncate(x) && !double.IsInfinity(x) && !double.IsNaN(x))
        {
            return x.ToString("0.0", CultureInfo.InvariantCulture);
        }

        return x.ToString(CultureInfo.InvariantCulture);
    }
}
