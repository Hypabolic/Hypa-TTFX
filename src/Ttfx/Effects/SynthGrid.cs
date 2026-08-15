using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>synthgrid, ported from effects/effect_synthgrid.py. Transcribed from <c>effects/synthgrid.rs</c>.</summary>
public sealed class SynthGridConfig
{
    public List<Color> GridGradientStops { get; set; } = new List<Color>();
    public List<long> GridGradientSteps { get; set; } = new List<long>();
    public GradientDirection GridGradientDirection { get; set; } = GradientDirection.Diagonal;
    public List<Color> TextGradientStops { get; set; } = new List<Color>();
    public List<long> TextGradientSteps { get; set; } = new List<long>();
    public GradientDirection TextGradientDirection { get; set; } = GradientDirection.Vertical;
    public string GridRowSymbol { get; set; } = "─";
    public string GridColumnSymbol { get; set; } = "│";
    public List<string> TextGenerationSymbols { get; set; } = new List<string>();
    public double MaxActiveBlocks { get; set; } = 0.1;
}

public enum GridLineDirection
{
    Horizontal,
    Vertical,
}

public enum SynthGridPhase
{
    GridExpand,
    AddChars,
    Collapse,
    Complete,
}

public sealed class GridLine
{
    public GridLineDirection Direction { get; }
    public List<CharId> Characters { get; }
    public List<CharId> CollapsedCharacters { get; }
    public List<CharId> ExtendedCharacters { get; } = new List<CharId>();

    public GridLine(GridLineDirection direction, List<CharId> characters, List<CharId> collapsedCharacters)
    {
        Direction = direction;
        Characters = characters;
        CollapsedCharacters = collapsedCharacters;
    }

    public void Extend(EngineWorld world)
    {
        int count = Direction == GridLineDirection.Horizontal ? 3 : 1;
        for (int i = 0; i < count; i++)
        {
            if (CollapsedCharacters.Count > 0)
            {
                CharId nextChar = CollapsedCharacters[0];
                CollapsedCharacters.RemoveAt(0);
                world.Terminal.SetCharacterVisibility(nextChar, true);
                ExtendedCharacters.Add(nextChar);
            }
        }
    }

    public void Collapse(EngineWorld world)
    {
        int count = Direction == GridLineDirection.Horizontal ? 3 : 1;
        if (CollapsedCharacters.Count == 0)
        {
            ExtendedCharacters.Reverse();
        }

        for (int i = 0; i < count; i++)
        {
            if (ExtendedCharacters.Count > 0)
            {
                CharId nextChar = ExtendedCharacters[0];
                ExtendedCharacters.RemoveAt(0);
                world.Terminal.SetCharacterVisibility(nextChar, false);
                CollapsedCharacters.Add(nextChar);
            }
        }
    }

    public bool IsExtended => CollapsedCharacters.Count == 0;

    public bool IsCollapsed => ExtendedCharacters.Count == 0;
}

public sealed class SynthGrid : IEffect
{
    /// <summary>update_group_tracker(group_number) — decrements the tracker.</summary>
    private const uint CbUpdateGroupTracker = 0;

    private readonly SynthGridConfig _config;
    private List<(long GroupNumber, List<CharId> Chars)> _pendingGroups = new List<(long, List<CharId>)>();
    private List<GridLine> _gridLines = new List<GridLine>();
    private List<long> _groupTracker = new List<long>();
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    private SynthGridPhase _phase;
    private int _totalGroupCount;
    private long _activeGroups;

    public SynthGrid(SynthGridConfig config)
    {
        _config = config;
        _phase = SynthGridPhase.GridExpand;
        _totalGroupCount = 0;
        _activeGroups = 0;
    }

    public static SynthGrid FromOptions(Dictionary<string, object> options)
    {
        return new SynthGrid(new SynthGridConfig
        {
            GridGradientStops = TypedList<Color>(options, "--grid-gradient-stops"),
            GridGradientSteps = TypedList<long>(options, "--grid-gradient-steps"),
            GridGradientDirection = (GradientDirection)options["--grid-gradient-direction"],
            TextGradientStops = TypedList<Color>(options, "--text-gradient-stops"),
            TextGradientSteps = TypedList<long>(options, "--text-gradient-steps"),
            TextGradientDirection = (GradientDirection)options["--text-gradient-direction"],
            GridRowSymbol = (string)options["--grid-row-symbol"],
            GridColumnSymbol = (string)options["--grid-column-symbol"],
            TextGenerationSymbols = TypedList<string>(options, "--text-generation-symbols"),
            MaxActiveBlocks = (double)options["--max-active-blocks"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
        if (callback.Id == CbUpdateGroupTracker
            && callback.Args.Length > 0
            && callback.Args[0] is CallbackValue.Int groupNumber)
        {
            _groupTracker[(int)groupNumber.Value] -= 1;
        }
    }

    private static long FindEvenGap(long dimension)
    {
        dimension -= 2;
        if (dimension <= 0)
        {
            return 0;
        }

        var potentialGaps = new List<long>();
        for (long i = dimension; i > 4; i--)
        {
            if (dimension % i <= 1)
            {
                potentialGaps.Add(i);
            }
        }

        if (potentialGaps.Count == 0)
        {
            return 4;
        }

        long target = PyCompat.FloorDiv(dimension, 5);
        long best = potentialGaps[0];
        long bestKey = System.Math.Abs(potentialGaps[0] - target);
        for (int j = 1; j < potentialGaps.Count; j++)
        {
            long key = System.Math.Abs(potentialGaps[j] - target);
            if (key < bestKey)
            {
                best = potentialGaps[j];
                bestKey = key;
            }
        }

        return best;
    }

    private GridLine MakeGridLine(
        EngineWorld world,
        Coord origin,
        GridLineDirection direction,
        CoordColorMap gridGradientMapping)
    {
        string gridSymbol = direction == GridLineDirection.Horizontal
            ? _config.GridRowSymbol
            : _config.GridColumnSymbol;
        var characters = new List<CharId>();
        long left = world.Terminal.Canvas.Left;
        long right = world.Terminal.Canvas.Right;
        long bottom = world.Terminal.Canvas.Bottom;
        long top = world.Terminal.Canvas.Top;
        IEnumerable<Coord> coords = direction == GridLineDirection.Horizontal
            ? RangeInclusive(left, right, column => Coord.New(column, origin.Row))
            : RangeExclusiveEnd(bottom, top, row => Coord.New(origin.Column, row));
        foreach (Coord coord in coords)
        {
            CharId effectChar = world.Terminal.AddCharacter(gridSymbol, Coord.New(0, 0));
            string gridScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)effectChar.Value];
                bool usesPre = ch.UsesInputPreexistingColors;
                gridScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                Color fg = gridGradientMapping.Get(coord)
                    ?? throw new EngineInvariantException("grid gradient mapping missing coord");
                ch.Animation.Scenes.Get(gridScn)!
                    .AddFrame(gridSymbol, 1, new VisualParams { Colors = ColorPair.New(fg, null) });
            }

            world.ActivateScene(this, effectChar, gridScn);
            EffectCharacter chMut = world.Terminal.Arena[(int)effectChar.Value];
            chMut.Layer = 2;
            chMut.Motion.SetCoordinate(coord);
            characters.Add(effectChar);
        }

        return new GridLine(direction, characters, new List<CharId>(characters));
    }

    private static IEnumerable<Coord> RangeInclusive(long start, long end, Func<long, Coord> map)
    {
        for (long i = start; i <= end; i++)
        {
            yield return map(i);
        }
    }

    /// <summary>Rust <c>bottom..top</c> — inclusive start, exclusive end.</summary>
    private static IEnumerable<Coord> RangeExclusiveEnd(long start, long endExclusive, Func<long, Coord> map)
    {
        for (long i = start; i < endExclusive; i++)
        {
            yield return map(i);
        }
    }

    public void Build(EngineWorld world)
    {
        Gradient gridGradient = Gradient.New(
            _config.GridGradientStops,
            _config.GridGradientSteps,
            false,
            false);
        CoordColorMap gridGradientMapping = gridGradient.BuildCoordinateColorMapping(
            1,
            world.Terminal.Canvas.Top,
            1,
            world.Terminal.Canvas.Right,
            _config.GridGradientDirection);
        Gradient textGradient = Gradient.New(
            _config.TextGradientStops,
            _config.TextGradientSteps,
            false,
            false);
        CoordColorMap textGradientMapping = textGradient.BuildCoordinateColorMapping(
            world.Terminal.Canvas.TextBottom,
            world.Terminal.Canvas.TextTop,
            world.Terminal.Canvas.TextLeft,
            world.Terminal.Canvas.TextRight,
            _config.TextGradientDirection);
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ColorPair colors = dynamic
                ? ColorPair.New(ch.Animation.InputFgColor, ch.Animation.InputBgColor)
                : ch.InputSymbol != " "
                    ? ColorPair.New(textGradientMapping.Get(ch.InputCoord), null)
                    : new ColorPair();
            _characterFinalColorMap[id] = colors;
        }

        long canvasLeft = world.Terminal.Canvas.Left;
        long canvasRight = world.Terminal.Canvas.Right;
        long canvasBottom = world.Terminal.Canvas.Bottom;
        long canvasTop = world.Terminal.Canvas.Top;
        _gridLines.Add(MakeGridLine(world, Coord.New(canvasLeft, canvasBottom), GridLineDirection.Horizontal, gridGradientMapping));
        _gridLines.Add(MakeGridLine(world, Coord.New(canvasLeft, canvasTop), GridLineDirection.Horizontal, gridGradientMapping));
        _gridLines.Add(MakeGridLine(world, Coord.New(canvasLeft, canvasBottom), GridLineDirection.Vertical, gridGradientMapping));
        _gridLines.Add(MakeGridLine(world, Coord.New(canvasRight, canvasBottom), GridLineDirection.Vertical, gridGradientMapping));

        long rowGap;
        long columnGap;
        if (canvasTop > 2 * canvasRight)
        {
            rowGap = FindEvenGap(canvasTop) + 1;
            columnGap = rowGap * 2;
        }
        else
        {
            columnGap = FindEvenGap(canvasRight) + 1;
            rowGap = PyCompat.FloorDiv(columnGap, 2);
        }

        var rowIndexes = new List<long>();
        var columnIndexes = new List<long>();
        long rowStep = System.Math.Max(rowGap, 1);
        for (long rowIndex = canvasBottom + rowGap; rowIndex < canvasTop; rowIndex += rowStep)
        {
            if (canvasTop - rowIndex >= 2)
            {
                rowIndexes.Add(rowIndex);
                _gridLines.Add(MakeGridLine(
                    world,
                    Coord.New(canvasLeft, rowIndex),
                    GridLineDirection.Horizontal,
                    gridGradientMapping));
            }
        }

        long columnStep = System.Math.Max(columnGap, 1);
        for (long columnIndex = canvasLeft + columnGap; columnIndex < canvasRight; columnIndex += columnStep)
        {
            if (canvasRight - columnIndex >= 2)
            {
                columnIndexes.Add(columnIndex);
                _gridLines.Add(MakeGridLine(
                    world,
                    Coord.New(columnIndex, canvasBottom),
                    GridLineDirection.Vertical,
                    gridGradientMapping));
            }
        }

        rowIndexes.Add(canvasTop + 1);
        columnIndexes.Add(canvasRight + 1);
        long prevRowIndex = 1;
        foreach (long rowIndexValue in rowIndexes)
        {
            long rowIndex = rowIndexValue;
            long prevColumnIndex = 1;
            foreach (long columnIndex in columnIndexes)
            {
                var coordsInBlock = new List<Coord>();
                if (rowIndex == canvasTop)
                {
                    rowIndex += 1;
                }

                for (long row = prevRowIndex; row < rowIndex; row++)
                {
                    for (long column = prevColumnIndex; column < columnIndex; column++)
                    {
                        coordsInBlock.Add(Coord.New(column, row));
                    }
                }

                var charactersInBlock = new List<CharId>();
                foreach (Coord coord in coordsInBlock)
                {
                    if (world.Terminal.CharacterByInputCoord.TryGetValue(coord, out CharId id))
                    {
                        charactersInBlock.Add(id);
                    }
                }

                if (charactersInBlock.Count > 0)
                {
                    _pendingGroups.Add((_pendingGroups.Count, charactersInBlock));
                }

                prevColumnIndex = columnIndex;
            }

            prevRowIndex = rowIndex;
        }

        _groupTracker = new List<long>(new long[_pendingGroups.Count]);
        var builtGroups = new List<(long, List<CharId>)>(_pendingGroups);
        _pendingGroups.Clear();
        foreach ((long groupNumber, List<CharId> group) in builtGroups)
        {
            foreach (CharId character in group)
            {
                string inputSymbol;
                bool usesPre;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)character.Value];
                    inputSymbol = ch.InputSymbol;
                    usesPre = ch.UsesInputPreexistingColors;
                }

                string dissolveScn = world.Terminal.Arena[(int)character.Value].Animation
                    .NewScene(false, null, null, "", usesPre);
                long frameCount = world.Rng.Randint(15, 30);
                Scene scene = world.Terminal.Arena[(int)character.Value].Animation.Scenes.Get(dissolveScn)
                    ?? throw new EngineInvariantException("dissolve scene");
                for (long i = 0; i < frameCount; i++)
                {
                    string symbol = world.Rng.Choice(_config.TextGenerationSymbols);
                    Color fg = world.Rng.Choice(textGradient.Spectrum);
                    scene.AddFrame(symbol, 2, new VisualParams { Colors = ColorPair.New(fg, null) });
                }

                ColorPair finalColors = _characterFinalColorMap.GetValueOrDefault(character, new ColorPair());
                scene.AddFrame(inputSymbol, 1, new VisualParams { Colors = finalColors });
                world.ActivateScene(this, character, dissolveScn);
                // synthgrid.rs:454-462 — payload is group_number at registration, not a loop capture.
                world.RegisterEvent(
                    character,
                    Event.SceneComplete,
                    new CallerKey.Scene(dissolveScn),
                    new EventAction.Callback(
                        new EffectCallback(CbUpdateGroupTracker, [new CallbackValue.Int(groupNumber)])));
            }
        }

        _pendingGroups = builtGroups;
        world.Rng.Shuffle(_pendingGroups);
        _phase = SynthGridPhase.GridExpand;
        _totalGroupCount = _pendingGroups.Count;
        if (_totalGroupCount == 0)
        {
            characters = world.Terminal.GetCharacters(
                world.Rng,
                CharacterFilter.Default,
                CharacterSort.TopToBottomLeftToRight);
            foreach (CharId character in characters)
            {
                world.Terminal.SetCharacterVisibility(character, true);
                world.ActiveCharacters.Insert(
                    character,
                    world.Terminal.Arena[(int)character.Value].CharacterId);
            }
        }

        _activeGroups = 0;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingGroups.Count > 0 || !world.ActiveCharacters.IsEmpty || _phase != SynthGridPhase.Complete)
        {
            switch (_phase)
            {
                case SynthGridPhase.GridExpand:
                    if (!_gridLines.TrueForAll(line => line.IsExtended))
                    {
                        var gridLines = new List<GridLine>(_gridLines);
                        _gridLines.Clear();
                        foreach (GridLine gridLine in gridLines)
                        {
                            if (!gridLine.IsExtended)
                            {
                                gridLine.Extend(world);
                            }

                            _gridLines.Add(gridLine);
                        }
                    }
                    else
                    {
                        _phase = SynthGridPhase.AddChars;
                    }

                    break;
                case SynthGridPhase.AddChars:
                    if (_pendingGroups.Count > 0
                        && _activeGroups < _totalGroupCount * _config.MaxActiveBlocks)
                    {
                        (long groupNumber, List<CharId> nextGroup) = _pendingGroups[0];
                        _pendingGroups.RemoveAt(0);
                        foreach (CharId ch in nextGroup)
                        {
                            world.Terminal.SetCharacterVisibility(ch, true);
                            world.ActiveCharacters.Insert(
                                ch,
                                world.Terminal.Arena[(int)ch.Value].CharacterId);
                            _groupTracker[(int)groupNumber] += 1;
                        }
                    }

                    if (_pendingGroups.Count == 0
                        && world.ActiveCharacters.IsEmpty
                        && _activeGroups == 0)
                    {
                        _phase = SynthGridPhase.Collapse;
                    }

                    break;
                case SynthGridPhase.Collapse:
                    if (!_gridLines.TrueForAll(line => line.IsCollapsed))
                    {
                        var gridLines = new List<GridLine>(_gridLines);
                        _gridLines.Clear();
                        foreach (GridLine gridLine in gridLines)
                        {
                            if (!gridLine.IsCollapsed)
                            {
                                gridLine.Collapse(world);
                            }

                            _gridLines.Add(gridLine);
                        }
                    }
                    else
                    {
                        _phase = SynthGridPhase.Complete;
                    }

                    break;
            }

            world.Update(this);
            _activeGroups = 0;
            foreach (long activeCount in _groupTracker)
            {
                if (activeCount != 0)
                {
                    _activeGroups += 1;
                }
            }

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
