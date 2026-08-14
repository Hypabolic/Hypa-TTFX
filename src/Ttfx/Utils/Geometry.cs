using System;

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
/// Geometry helpers needed by the colour pipeline. Full geometry is issue 0009.
/// Transcribed from <c>utils/geometry.rs</c>.
/// </summary>
public static class Geometry
{
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
