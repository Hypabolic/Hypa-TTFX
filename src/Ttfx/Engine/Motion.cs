using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Motion: per-character movement state. Only <c>current_coord</c> / <c>set_coordinate</c>
/// are needed for <c>--m0-dump</c>. Path stepping is a later issue.
/// Transcribed from <c>engine/motion.rs</c>.
/// </summary>
public sealed class Motion
{
    public Coord CurrentCoord { get; set; }

    public Coord PreviousCoord { get; set; }

    private Motion(Coord inputCoord)
    {
        CurrentCoord = inputCoord;
        PreviousCoord = Coord.New(-1, -1);
    }

    public static Motion New(Coord inputCoord) => new Motion(inputCoord);

    public void SetCoordinate(Coord coord)
    {
        CurrentCoord = coord;
    }
}
