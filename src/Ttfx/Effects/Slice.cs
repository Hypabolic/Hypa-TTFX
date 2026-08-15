using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>slice, ported from effects/effect_slice.py. Transcribed from <c>effects/slice.rs</c>.</summary>
public sealed class SliceConfig
{
    public string SliceDirection { get; set; } = "vertical";
    public double MovementSpeed { get; set; } = 0.25;
    public Easing MovementEasing { get; set; } = Easing.InOutExpo;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
}

public sealed class Slice : IEffect
{
    private readonly SliceConfig _config;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;

    public Slice(SliceConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    }

    /// <summary>slice.rs slice_direction value_parser.</summary>
    public static object ParseSliceDirection(string s)
    {
        return s switch
        {
            "vertical" => "vertical",
            "horizontal" => "horizontal",
            "diagonal" => "diagonal",
            _ => throw new UsageError($"invalid choice: '{s}' (choose from 'vertical', 'horizontal', 'diagonal')"),
        };
    }

    public static Slice FromOptions(Dictionary<string, object> options)
    {
        return new Slice(new SliceConfig
        {
            SliceDirection = (string)options["--slice-direction"],
            MovementSpeed = (double)options["--movement-speed"],
            MovementEasing = (Easing)options["--movement-easing"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    public void Build(EngineWorld world)
    {
        Gradient finalGradient = Gradient.New(
            _config.FinalGradientStops,
            _config.FinalGradientSteps,
            false,
            false);
        CoordColorMap finalGradientMapping = finalGradient.BuildCoordinateColorMapping(
            world.Terminal.Canvas.TextBottom,
            world.Terminal.Canvas.TextTop,
            world.Terminal.Canvas.TextLeft,
            world.Terminal.Canvas.TextRight,
            _config.FinalGradientDirection);

        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        List<CharId> characters;
        {
            CharacterFilter filter = CharacterFilter.Default;
            characters = world.Terminal.GetCharacters(
                world.Rng,
                filter,
                CharacterSort.TopToBottomLeftToRight);
        }

        foreach (CharId id in characters)
        {
            Color? inputFg;
            Color? inputBg;
            Coord inputCoord;
            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
            }

            ColorPair finalColors;
            if (dynamic)
            {
                finalColors = ColorPair.New(inputFg, inputBg);
            }
            else
            {
                Color mapped = finalGradientMapping.Get(inputCoord)
                    ?? throw new EngineInvariantException("gradient mapping missing");
                finalColors = ColorPair.New(mapped, null);
            }

            _characterFinalColorMap[id] = finalColors;
            world.Terminal.Arena[(int)id.Value].Animation.SetAppearance(inputSymbol, usesPre, inputSymbol, finalColors);
        }

        double movementSpeed = _config.MovementSpeed;
        Easing movementEasing = _config.MovementEasing;

        void SendTo(CharId id, Coord origin)
        {
            Coord inputCoord = world.Terminal.Arena[(int)id.Value].InputCoord;
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ch.Motion.SetCoordinate(origin);
            string pathId = ch.Motion.NewPath(
                movementSpeed,
                movementEasing,
                null,
                0,
                false,
                "");
            Path path = ch.Motion.Paths.Get(pathId)
                ?? throw new EngineInvariantException("input coord path");
            path.NewWaypoint(inputCoord, null, "");
            world.ActivatePath(this, id, pathId);
        }

        void ExtendActive(IEnumerable<CharId> ids)
        {
            foreach (CharId id in ids)
            {
                world.ActiveCharacters.Insert(
                    id,
                    world.Terminal.Arena[(int)id.Value].CharacterId);
            }
        }

        long canvasTop = world.Terminal.Canvas.Top;
        long canvasBottom = world.Terminal.Canvas.Bottom;
        long canvasLeft = world.Terminal.Canvas.Left;
        long canvasRight = world.Terminal.Canvas.Right;
        long textCenterColumn = world.Terminal.Canvas.TextCenterColumn;
        long textCenterRow = world.Terminal.Canvas.TextCenterRow;
        long textLeft = world.Terminal.Canvas.TextLeft;
        long textRight = world.Terminal.Canvas.TextRight;
        long textTop = world.Terminal.Canvas.TextTop;
        long textBottom = world.Terminal.Canvas.TextBottom;

        if (_config.SliceDirection == "vertical")
        {
            List<List<CharId>> rows = world.Terminal.GetCharactersGrouped(
                CharacterFilter.Default,
                CharacterGroup.RowBottomToTop);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<CharId> row = rows[rowIndex];
                var leftHalf = new List<CharId>();
                foreach (CharId id in row)
                {
                    if (world.Terminal.Arena[(int)id.Value].InputCoord.Column <= textCenterColumn)
                    {
                        leftHalf.Add(id);
                    }
                }

                foreach (CharId id in leftHalf)
                {
                    long column = world.Terminal.Arena[(int)id.Value].InputCoord.Column;
                    SendTo(id, Coord.New(column, canvasTop + 1));
                }

                List<CharId> oppositeRow = rows[rows.Count - (rowIndex + 1)];
                var rightHalf = new List<CharId>();
                foreach (CharId id in oppositeRow)
                {
                    if (world.Terminal.Arena[(int)id.Value].InputCoord.Column > textCenterColumn)
                    {
                        rightHalf.Add(id);
                    }
                }

                foreach (CharId id in rightHalf)
                {
                    long column = world.Terminal.Arena[(int)id.Value].InputCoord.Column;
                    SendTo(id, Coord.New(column, canvasBottom - 1));
                }

                ExtendActive(leftHalf);
                ExtendActive(rightHalf);
            }
        }
        else if (_config.SliceDirection == "horizontal")
        {
            movementSpeed *= 2.0;
            CharacterFilter columnFilter = new CharacterFilter(true, true, true, false);
            List<List<CharId>> columns = world.Terminal.GetCharactersGrouped(
                columnFilter,
                CharacterGroup.ColumnRightToLeft);
            var trimmedColumns = new List<List<CharId>>();
            foreach (List<CharId> column in columns)
            {
                var newColumn = new List<CharId>();
                foreach (CharId id in column)
                {
                    Coord c = world.Terminal.Arena[(int)id.Value].InputCoord;
                    if (textLeft <= c.Column && c.Column <= textRight
                        && textBottom <= c.Row && c.Row <= textTop)
                    {
                        newColumn.Add(id);
                    }
                }

                if (newColumn.Count > 0)
                {
                    trimmedColumns.Add(newColumn);
                }
            }

            columns = trimmedColumns;
            long midPoint = textCenterRow;
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                List<CharId> column = columns[columnIndex];
                var bottomHalf = new List<CharId>();
                foreach (CharId id in column)
                {
                    if (world.Terminal.Arena[(int)id.Value].InputCoord.Row <= midPoint)
                    {
                        bottomHalf.Add(id);
                    }
                }

                foreach (CharId id in bottomHalf)
                {
                    long row = world.Terminal.Arena[(int)id.Value].InputCoord.Row;
                    SendTo(id, Coord.New(canvasLeft - 1, row));
                }

                List<CharId> oppositeColumn = columns[columns.Count - (columnIndex + 1)];
                var topHalf = new List<CharId>();
                foreach (CharId id in oppositeColumn)
                {
                    if (world.Terminal.Arena[(int)id.Value].InputCoord.Row > midPoint)
                    {
                        topHalf.Add(id);
                    }
                }

                foreach (CharId id in topHalf)
                {
                    long row = world.Terminal.Arena[(int)id.Value].InputCoord.Row;
                    SendTo(id, Coord.New(canvasRight + 1, row));
                }

                ExtendActive(bottomHalf);
                ExtendActive(topHalf);
            }
        }
        else if (_config.SliceDirection == "diagonal")
        {
            List<List<CharId>> diagonals = world.Terminal.GetCharactersGrouped(
                CharacterFilter.Default,
                CharacterGroup.DiagonalBottomLeftToTopRight);
            var left = new List<List<CharId>>(diagonals.GetRange(0, diagonals.Count / 2));
            var right = new List<List<CharId>>(diagonals.GetRange(diagonals.Count / 2, diagonals.Count - diagonals.Count / 2));
            while (left.Count > 0 || right.Count > 0)
            {
                if (left.Count > 0)
                {
                    // slice.rs:230 — remove(0) FIFO drain
                    List<CharId> leftGroup = left[0];
                    left.RemoveAt(0);
                    Coord originCoord = Coord.New(
                        world.Terminal.Arena[(int)leftGroup[0].Value].InputCoord.Column,
                        canvasBottom - 1);
                    foreach (CharId id in leftGroup)
                    {
                        SendTo(id, originCoord);
                    }

                    ExtendActive(leftGroup);
                }

                if (right.Count > 0)
                {
                    // slice.rs:241 — remove(0) FIFO drain
                    List<CharId> rightGroup = right[0];
                    right.RemoveAt(0);
                    Coord originCoord = Coord.New(
                        world.Terminal.Arena[(int)rightGroup[^1].Value].InputCoord.Column,
                        canvasTop + 1);
                    foreach (CharId id in rightGroup)
                    {
                        SendTo(id, originCoord);
                    }

                    ExtendActive(rightGroup);
                }
            }
        }

        foreach (CharId id in world.ActiveCharacters.Snapshot())
        {
            world.Terminal.SetCharacterVisibility(id, true);
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!world.ActiveCharacters.IsEmpty)
        {
            world.Update(this);
            return world.Frame();
        }

        return null;
    }

    private static List<T> TypedList<T>(Dictionary<string, object> options, string key)
    {
        var raw = (List<object>)options[key];
        var result = new List<T>(raw.Count);
        foreach (object item in raw)
        {
            result.Add((T)item);
        }

        return result;
    }
}
