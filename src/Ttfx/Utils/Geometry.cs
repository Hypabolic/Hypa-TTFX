namespace Ttfx.Utils;

/// <summary>
/// 1-based canvas coordinate: column grows right, row grows UP (origin bottom-left).
/// Transcribed from <c>utils/geometry.rs</c>.
/// </summary>
public readonly record struct Coord(long Column, long Row)
{
    public static Coord New(long column, long row) => new Coord(column, row);
}
