using System;
using System.Collections.Generic;

namespace Ttfx.Utils;

/// <summary>
/// 1-based canvas coordinate: column grows right, row grows UP (origin bottom-left).
/// Transcribed from <c>utils/geometry.rs</c>.
/// </summary>
public readonly record struct Coord(long Column, long Row)
{
    public static Coord New(long column, long row) => new Coord(column, row);
}

/// <summary>
/// Float-valued point for bezier intermediates: upstream builds Coord objects
/// with float fields inside de_casteljau (violating its own annotation) and
/// only rounds the final result.
/// </summary>
internal readonly struct FloatPoint
{
    public readonly double Column;
    public readonly double Row;

    public FloatPoint(double column, double row)
    {
        Column = column;
        Row = row;
    }

    public FloatPoint Interpolate(FloatPoint other, double t)
        => new FloatPoint((1.0 - t) * Column + t * other.Column, (1.0 - t) * Row + t * other.Row);
}

/// <summary>
/// Coord and geometry math, ported from utils/geometry.py.
/// Transcribed from <c>utils/geometry.rs</c>.
/// </summary>
/// <remarks>
/// Upstream wraps every function in lru_cache; behavior is identical without
/// the caches, so they are omitted. All <c>round()</c> calls are banker's rounding
/// (pycompat), all int() casts truncate.
/// </remarks>
public static class Geometry
{
    /// <summary>
    /// find_coords_on_circle: coords_limit 0 -> round(2*pi*r); x offset from the
    /// origin is doubled for cell aspect; every point rounded (banker's).
    /// </summary>
    public static List<Coord> FindCoordsOnCircle(Coord origin, long radius, long coordsLimit, bool unique)
    {
        var points = new List<Coord>();
        if (radius == 0)
        {
            return points;
        }

        var seen = new HashSet<Coord>();
        if (coordsLimit == 0)
        {
            coordsLimit = PyCompat.RoundHalfEven(2.0 * Math.PI * radius);
        }

        double angleStep = 2.0 * Math.PI / coordsLimit;
        for (long i = 0; i < coordsLimit; i++)
        {
            double angle = angleStep * i;
            double x = origin.Column + radius * Math.Cos(angle);
            double xDiff = x - origin.Column;
            x += xDiff;
            double y = origin.Row + radius * Math.Sin(angle);
            Coord point = Coord.New(PyCompat.RoundHalfEven(x), PyCompat.RoundHalfEven(y));
            if (unique)
            {
                if (!seen.Contains(point))
                {
                    points.Add(point);
                }
            }
            else
            {
                points.Add(point);
            }

            seen.Add(point);
        }

        return points;
    }

    /// <summary>
    /// find_coords_in_circle: actually an ellipse (a = diameter, b = diameter/2);
    /// int() truncation on the y offset, faithfully.
    /// </summary>
    public static List<Coord> FindCoordsInCircle(Coord center, long diameter)
    {
        long h = center.Column;
        long k = center.Row;
        var coords = new List<Coord>();
        if (diameter == 0)
        {
            return coords;
        }

        double aSquared = Math.Pow(diameter, 2.0);
        double bSquared = Math.Pow(diameter / 2.0, 2.0);
        for (long x = h - diameter; x <= h + diameter; x++)
        {
            double xComponent = Math.Pow(x - h, 2.0) / aSquared;
            long maxYOffset = PyCompat.TruncToI64(Math.Pow(bSquared * (1.0 - xComponent), 0.5));
            for (long y = k - maxYOffset; y <= k + maxYOffset; y++)
            {
                coords.Add(Coord.New(x, y));
            }
        }

        return coords;
    }

    /// <summary>
    /// find_coords_in_rect: full (2d+1)^2 block, empty for distance 0.
    /// Iteration order is column-major like upstream.
    /// </summary>
    public static List<Coord> FindCoordsInRect(Coord origin, long distance)
    {
        var coords = new List<Coord>();
        if (distance == 0)
        {
            return coords;
        }

        for (long column = origin.Column - distance; column <= origin.Column + distance; column++)
        {
            for (long row = origin.Row - distance; row <= origin.Row + distance; row++)
            {
                coords.Add(Coord.New(column, row));
            }
        }

        return coords;
    }

    /// <summary>
    /// find_coords_on_rect: perimeter only; empty if either half-dimension is 0.
    /// </summary>
    public static List<Coord> FindCoordsOnRect(Coord origin, long halfWidth, long halfHeight)
    {
        var coords = new List<Coord>();
        if (halfWidth == 0 || halfHeight == 0)
        {
            return coords;
        }

        for (long column = origin.Column - halfWidth; column <= origin.Column + halfWidth; column++)
        {
            if (column == origin.Column - halfWidth || column == origin.Column + halfWidth)
            {
                for (long row = origin.Row - halfHeight; row <= origin.Row + halfHeight; row++)
                {
                    coords.Add(Coord.New(column, row));
                }
            }
            else
            {
                coords.Add(Coord.New(column, origin.Row - halfHeight));
                coords.Add(Coord.New(column, origin.Row + halfHeight));
            }
        }

        return coords;
    }

    /// <summary>
    /// extrapolate_along_ray: NON-doubled line length, lerp past the target, round.
    /// </summary>
    public static Coord ExtrapolateAlongRay(Coord origin, Coord target, double offsetFromTarget)
    {
        double baseLen = FindLengthOfLine(origin, target, false);
        double totalDistance = baseLen + offsetFromTarget;
        if (totalDistance == 0.0 || origin == target)
        {
            return target;
        }

        double t = totalDistance / baseLen;
        double nextColumn = (1.0 - t) * origin.Column + t * target.Column;
        double nextRow = (1.0 - t) * origin.Row + t * target.Row;
        return Coord.New(PyCompat.RoundHalfEven(nextColumn), PyCompat.RoundHalfEven(nextRow));
    }

    /// <summary>
    /// find_coord_on_bezier_curve: recursive De Casteljau of arbitrary degree with
    /// float intermediates, rounded only at the end.
    /// </summary>
    public static Coord FindCoordOnBezierCurve(Coord start, IReadOnlyList<Coord> control, Coord end, double t)
    {
        if (control.Count == 0)
        {
            return FindCoordOnLine(start, end, t);
        }

        var startPt = new FloatPoint(start.Column, start.Row);
        var endPt = new FloatPoint(end.Column, end.Row);

        // Every production path is quadratic. Keep that per-frame hot path on the
        // stack instead of allocating a Vec at each De Casteljau level.
        if (control.Count == 1)
        {
            var ctrl = new FloatPoint(control[0].Column, control[0].Row);
            FloatPoint point = startPt.Interpolate(ctrl, t).Interpolate(ctrl.Interpolate(endPt, t), t);
            return Coord.New(PyCompat.RoundHalfEven(point.Column), PyCompat.RoundHalfEven(point.Row));
        }

        var points = new List<FloatPoint>(control.Count + 2);
        points.Add(startPt);
        foreach (Coord c in control)
        {
            points.Add(new FloatPoint(c.Column, c.Row));
        }

        points.Add(endPt);
        int remaining = points.Count;
        while (remaining > 1)
        {
            for (int i = 0; i < remaining - 1; i++)
            {
                points[i] = points[i].Interpolate(points[i + 1], t);
            }

            remaining -= 1;
        }

        return Coord.New(PyCompat.RoundHalfEven(points[0].Column), PyCompat.RoundHalfEven(points[0].Row));
    }

    /// <summary>find_coord_on_line: lerp + round.</summary>
    public static Coord FindCoordOnLine(Coord start, Coord end, double t)
    {
        double x = (1.0 - t) * start.Column + t * end.Column;
        double y = (1.0 - t) * start.Row + t * end.Row;
        return Coord.New(PyCompat.RoundHalfEven(x), PyCompat.RoundHalfEven(y));
    }

    /// <summary>
    /// find_length_of_bezier_curve: 10-sample polyline that stops at t=0.9 — the
    /// final t=0.9..1.0 span is deliberately (faithfully) omitted, systematically
    /// underestimating lengths. Do not fix (plan.md §5.4).
    /// </summary>
    public static double FindLengthOfBezierCurve(Coord start, IReadOnlyList<Coord> control, Coord end)
    {
        double length = 0.0;
        Coord prevCoord = start;
        for (int t = 1; t < 10; t++)
        {
            Coord coord = FindCoordOnBezierCurve(start, control, end, t / 10.0);
            length += FindLengthOfLine(prevCoord, coord, true);
            prevCoord = coord;
        }

        return length;
    }

    /// <summary>
    /// find_length_of_line: hypot, with the row delta doubled when requested
    /// (terminal cell aspect convention).
    /// </summary>
    public static double FindLengthOfLine(Coord coord1, Coord coord2, bool doubleRowDiff)
    {
        double columnDiff = coord2.Column - coord1.Column;
        double rowDiff = coord2.Row - coord1.Row;
        if (doubleRowDiff)
        {
            return double.Hypot(columnDiff, 2.0 * rowDiff);
        }

        return double.Hypot(columnDiff, rowDiff);
    }

    /// <summary>
    /// find_normalized_distance_from_center: rejects out-of-rectangle coords
    /// (upstream ValueError); stays within [0, 1] for accepted ones.
    /// </summary>
    public static double FindNormalizedDistanceFromCenter(
        long bottom,
        long top,
        long left,
        long right,
        Coord otherCoord)
    {
        long yOffset = bottom - 1;
        long xOffset = left - 1;
        right -= xOffset;
        top -= yOffset;
        double centerX = right / 2.0;
        double centerY = top / 2.0;

        // Python: `n not in range(a, b+1)` — integer membership
        long col = otherCoord.Column - xOffset;
        long row = otherCoord.Row - yOffset;
        if (col < left - xOffset || col > right || row < bottom - yOffset || row > top)
        {
            throw new ArgumentException("Coordinate is not within the rectangle.");
        }

        double maxDistance = Math.Pow(Math.Pow(right, 2.0) + Math.Pow(top * 2, 2.0), 0.5);
        double distance = Math.Pow(
            Math.Pow(col - centerX, 2.0) + Math.Pow((row - centerY) * 2.0, 2.0),
            0.5);
        return distance / (maxDistance / 2.0);
    }
}
