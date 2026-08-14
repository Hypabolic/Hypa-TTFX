using System;
using System.Collections.Generic;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Compass anchors. Parse is case-sensitive, matching <c>Anchor::parse</c>.
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

/// <summary>
/// Canvas, ported from engine/terminal.py Canvas.
/// Transcribed from <c>engine/canvas.rs</c>.
/// </summary>
public sealed class Canvas
{
    public long Top { get; }
    public long Right { get; }
    public long Bottom { get; }
    public long Left { get; }
    public long CenterRow { get; }
    public long CenterColumn { get; }
    public Coord Center { get; }
    public long Width { get; }
    public long Height { get; }
    public long TextLeft { get; set; }
    public long TextRight { get; set; }
    public long TextTop { get; set; }
    public long TextBottom { get; set; }
    public long TextWidth { get; set; }
    public long TextHeight { get; set; }
    public long TextCenterRow { get; set; }
    public long TextCenterColumn { get; set; }
    public Coord TextCenter { get; set; }

    /// <summary>Canvas.__post_init__ with bottom=1, left=1 defaults.</summary>
    public Canvas(long top, long right)
    {
        long bottom = 1;
        long left = 1;
        long centerRow = Math.Max(PyCompat.FloorDiv(top, 2), bottom);
        if (top % 2 != 0 && top > 1)
        {
            centerRow += 1;
        }

        long centerColumn = Math.Max(PyCompat.FloorDiv(right, 2), left);
        if (right % 2 != 0 && right > 1)
        {
            centerColumn += 1;
        }

        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
        CenterRow = centerRow;
        CenterColumn = centerColumn;
        Center = Coord.New(centerColumn, centerRow);
        Width = right;
        Height = top;
        TextLeft = 0;
        TextRight = 0;
        TextTop = 0;
        TextBottom = 0;
        TextWidth = 0;
        TextHeight = 0;
        TextCenterRow = 0;
        TextCenterColumn = 0;
        TextCenter = Coord.New(0, 0);
    }

    public static Canvas New(long top, long right) => new Canvas(top, right);

    /// <summary>
    /// Canvas._anchor_text: shift characters per the anchor, drop out-of-canvas
    /// ones, then compute text extents. <c>characters</c> must be non-empty after the
    /// in-canvas filter or this errors (upstream crashes on bare max()/min()).
    /// </summary>
    public List<CharId> AnchorText(List<EffectCharacter> arena, List<CharId> characters, Anchor anchor)
    {
        if (characters.Count == 0)
        {
            throw new EngineException("no input characters to anchor");
        }

        long inputWidth = long.MinValue;
        long inputHeight = long.MinValue;
        foreach (CharId id in characters)
        {
            Coord c = arena[(int)id.Value].InputCoord;
            if (c.Column > inputWidth)
            {
                inputWidth = c.Column;
            }

            if (c.Row > inputHeight)
            {
                inputHeight = c.Row;
            }
        }

        long columnDelta = 0;
        long rowDelta = 0;
        if (inputWidth != Width)
        {
            switch (anchor)
            {
                case Anchor.S:
                case Anchor.N:
                case Anchor.C:
                    columnDelta = CenterColumn - PyCompat.FloorDiv(inputWidth, 2);
                    break;
                case Anchor.Se:
                case Anchor.E:
                case Anchor.Ne:
                    columnDelta = Right - inputWidth;
                    break;
                case Anchor.Sw:
                case Anchor.W:
                case Anchor.Nw:
                    columnDelta = Left - 1;
                    break;
            }
        }

        if (inputHeight != Height)
        {
            switch (anchor)
            {
                case Anchor.W:
                case Anchor.E:
                case Anchor.C:
                    rowDelta = CenterRow - PyCompat.FloorDiv(inputHeight, 2);
                    break;
                case Anchor.Nw:
                case Anchor.N:
                case Anchor.Ne:
                    rowDelta = Top - inputHeight;
                    break;
                case Anchor.Sw:
                case Anchor.S:
                case Anchor.Se:
                    rowDelta = Bottom - 1;
                    break;
            }
        }

        foreach (CharId id in characters)
        {
            EffectCharacter ch = arena[(int)id.Value];
            Coord anchored = Coord.New(ch.InputCoord.Column + columnDelta, ch.InputCoord.Row + rowDelta);
            ch.InputCoord = anchored;
            ch.Motion.SetCoordinate(anchored);
        }

        var kept = new List<CharId>();
        foreach (CharId id in characters)
        {
            if (CoordIsInCanvas(arena[(int)id.Value].InputCoord))
            {
                kept.Add(id);
            }
        }

        if (kept.Count == 0)
        {
            // Upstream raises ValueError from max() on an empty sequence here.
            throw new EngineException("all input characters fall outside the canvas after anchoring");
        }

        long textLeft = long.MaxValue;
        long textRight = long.MinValue;
        long textTop = long.MinValue;
        long textBottom = long.MaxValue;
        foreach (CharId id in kept)
        {
            Coord c = arena[(int)id.Value].InputCoord;
            if (c.Column < textLeft)
            {
                textLeft = c.Column;
            }

            if (c.Column > textRight)
            {
                textRight = c.Column;
            }

            if (c.Row > textTop)
            {
                textTop = c.Row;
            }

            if (c.Row < textBottom)
            {
                textBottom = c.Row;
            }
        }

        TextLeft = textLeft;
        TextRight = textRight;
        TextTop = textTop;
        TextBottom = textBottom;
        TextWidth = Math.Max(TextRight - TextLeft + 1, 1);
        TextHeight = Math.Max(TextTop - TextBottom + 1, 1);
        TextCenterRow = TextBottom + PyCompat.FloorDiv(TextTop - TextBottom, 2);
        TextCenterColumn = TextLeft + PyCompat.FloorDiv(TextRight - TextLeft, 2);
        TextCenter = Coord.New(TextCenterColumn, TextCenterRow);
        return kept;
    }

    public bool CoordIsInCanvas(Coord coord)
    {
        return Left <= coord.Column && coord.Column <= Right && Bottom <= coord.Row && coord.Row <= Top;
    }

    public bool CoordIsInText(Coord coord)
    {
        return TextLeft <= coord.Column
            && coord.Column <= TextRight
            && TextBottom <= coord.Row
            && coord.Row <= TextTop;
    }

    public long RandomColumn(Rng rng, bool withinTextBoundary)
    {
        return withinTextBoundary
            ? rng.Randint(TextLeft, TextRight)
            : rng.Randint(Left, Right);
    }

    public long RandomRow(Rng rng, bool withinTextBoundary)
    {
        return withinTextBoundary
            ? rng.Randint(TextBottom, TextTop)
            : rng.Randint(Bottom, Top);
    }

    /// <summary>
    /// random_coord: outside_scope picks among four coords exactly one cell past
    /// an edge — note the RNG call ORDER (above, below, left, right built first,
    /// then choice) is part of the parity contract.
    /// </summary>
    public Coord RandomCoord(Rng rng, bool outsideScope, bool withinTextBoundary)
    {
        if (outsideScope)
        {
            Coord above = Coord.New(RandomColumn(rng, false), Top + 1);
            Coord below = Coord.New(RandomColumn(rng, false), Bottom - 1);
            Coord left = Coord.New(Left - 1, RandomRow(rng, false));
            Coord right = Coord.New(Right + 1, RandomRow(rng, false));
            return rng.Choice(new[] { above, below, left, right });
        }

        return Coord.New(RandomColumn(rng, withinTextBoundary), RandomRow(rng, withinTextBoundary));
    }
}
