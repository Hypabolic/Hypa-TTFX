namespace Ttfx.Engine;

/// <summary>
/// Compass anchors. Parse is case-sensitive, matching <c>Anchor::parse</c>.
/// Layout math is issue 0006.
/// </summary>
public enum Anchor
{
    N,
    Ne,
    E,
    Se,
    S,
    Sw,
    W,
    Nw,
    C,
}

public static class AnchorParse
{
    public static Anchor? Parse(string s)
    {
        return s switch
        {
            "n" => Anchor.N,
            "ne" => Anchor.Ne,
            "e" => Anchor.E,
            "se" => Anchor.Se,
            "s" => Anchor.S,
            "sw" => Anchor.Sw,
            "w" => Anchor.W,
            "nw" => Anchor.Nw,
            "c" => Anchor.C,
            _ => null,
        };
    }
}
