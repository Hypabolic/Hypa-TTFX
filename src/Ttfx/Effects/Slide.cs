using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>typing.Literal["row", "column", "diagonal"].</summary>
public enum SlideGrouping
{
    Row,
    Column,
    Diagonal,
}

/// <summary>slide, ported from effects/effect_slide.py. Transcribed from <c>effects/slide.rs</c>.</summary>
public sealed class SlideConfig
{
    public double MovementSpeed { get; set; } = 0.8;
    public SlideGrouping Grouping { get; set; } = SlideGrouping.Row;
    public long Gap { get; set; } = 2;
    public bool ReverseDirection { get; set; }
    public bool Merge { get; set; }
    public Easing MovementEasing { get; set; } = Easing.InOutQuad;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 6;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Slide : IEffect
{
    private readonly SlideConfig _config;
    private readonly List<List<CharId>> _pendingGroups;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private readonly List<List<CharId>> _activeGroups;
    private long _currentGap;

    public Slide(SlideConfig config)
    {
        _config = config;
        _pendingGroups = new List<List<CharId>>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _activeGroups = new List<List<CharId>>();
        _currentGap = 0;
    }

    /// <summary>slide.rs parse_slide_grouping.</summary>
    public static object ParseSlideGrouping(string s)
    {
        return s switch
        {
            "row" => SlideGrouping.Row,
            "column" => SlideGrouping.Column,
            "diagonal" => SlideGrouping.Diagonal,
            _ => throw new UsageError($"invalid choice: '{s}' (choose from 'row', 'column', 'diagonal')"),
        };
    }

    public static Slide FromOptions(Dictionary<string, object> options)
    {
        return new Slide(new SlideConfig
        {
            MovementSpeed = (double)options["--movement-speed"],
            Grouping = (SlideGrouping)options["--grouping"],
            Gap = (long)options["--gap"],
            ReverseDirection = options.ContainsKey("--reverse-direction"),
            Merge = options.ContainsKey("--merge"),
            MovementEasing = (Easing)options["--movement-easing"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientFrames = (long)options["--final-gradient-frames"],
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
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                inputCoord = ch.InputCoord;
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
        }

        CharacterGroup grouping = _config.Grouping switch
        {
            SlideGrouping.Row => CharacterGroup.RowTopToBottom,
            SlideGrouping.Column => CharacterGroup.ColumnLeftToRight,
            SlideGrouping.Diagonal => CharacterGroup.DiagonalTopLeftToBottomRight,
            _ => throw new EngineInvariantException("slide grouping"),
        };
        List<List<CharId>> groups = world.Terminal.GetCharactersGrouped(CharacterFilter.Default, grouping);
        foreach (List<CharId> group in groups)
        {
            foreach (CharId id in group)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.NewPath(
                    _config.MovementSpeed,
                    _config.MovementEasing,
                    null,
                    0,
                    false,
                    "input_path");
                Path path = ch.Motion.Paths.Get("input_path")
                    ?? throw new EngineInvariantException("input_path");
                Coord inputCoord = ch.InputCoord;
                path.NewWaypoint(inputCoord, null, "");
            }
        }

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            // Python's loop variable `group` keeps the original (pre-reversal) list.
            List<CharId> group = new List<CharId>(groups[groupIndex]);
            switch (_config.Grouping)
            {
                case SlideGrouping.Row:
                {
                    long startingColumn;
                    if (_config.Merge && groupIndex % 2 == 0)
                    {
                        startingColumn = world.Terminal.Canvas.Right + 1;
                    }
                    else
                    {
                        groups[groupIndex].Reverse();
                        startingColumn = world.Terminal.Canvas.Left - 1;
                    }

                    if (_config.ReverseDirection && !_config.Merge)
                    {
                        groups[groupIndex].Reverse();
                        startingColumn = world.Terminal.Canvas.Right + 1;
                    }

                    for (int i = 0; i < groups[groupIndex].Count; i++)
                    {
                        CharId id = groups[groupIndex][i];
                        long row = world.Terminal.Arena[(int)id.Value].InputCoord.Row;
                        world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(
                            Coord.New(startingColumn, row));
                    }

                    break;
                }

                case SlideGrouping.Column:
                {
                    long startingRow;
                    if (_config.Merge && groupIndex % 2 == 0)
                    {
                        startingRow = world.Terminal.Canvas.Bottom - 1;
                    }
                    else
                    {
                        groups[groupIndex].Reverse();
                        startingRow = world.Terminal.Canvas.Top + 1;
                    }

                    if (_config.ReverseDirection && !_config.Merge)
                    {
                        groups[groupIndex].Reverse();
                        startingRow = world.Terminal.Canvas.Bottom - 1;
                    }

                    for (int i = 0; i < groups[groupIndex].Count; i++)
                    {
                        CharId id = groups[groupIndex][i];
                        long column = world.Terminal.Arena[(int)id.Value].InputCoord.Column;
                        world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(
                            Coord.New(column, startingRow));
                    }

                    break;
                }

                case SlideGrouping.Diagonal:
                    break;
            }

            if (_config.Grouping == SlideGrouping.Diagonal)
            {
                Coord lastCoord = world.Terminal.Arena[(int)group[^1].Value].InputCoord;
                long distanceFromOutsideBottom = lastCoord.Row - (world.Terminal.Canvas.Bottom - 1);
                Coord startingCoord = Coord.New(
                    lastCoord.Column - distanceFromOutsideBottom,
                    lastCoord.Row - distanceFromOutsideBottom);
                if (_config.Merge && groupIndex % 2 == 0)
                {
                    groups[groupIndex].Reverse();
                    Coord firstCoord = world.Terminal.Arena[(int)group[0].Value].InputCoord;
                    long distanceFromOutside = (world.Terminal.Canvas.Top + 1) - firstCoord.Row;
                    startingCoord = Coord.New(
                        firstCoord.Column + distanceFromOutside,
                        firstCoord.Row + distanceFromOutside);
                }

                if (_config.ReverseDirection && !_config.Merge)
                {
                    groups[groupIndex].Reverse();
                    Coord firstCoord = world.Terminal.Arena[(int)group[0].Value].InputCoord;
                    long distanceFromOutside = (world.Terminal.Canvas.Top + 1) - firstCoord.Row;
                    startingCoord = Coord.New(
                        firstCoord.Column + distanceFromOutside,
                        firstCoord.Row + distanceFromOutside);
                }

                for (int i = 0; i < groups[groupIndex].Count; i++)
                {
                    CharId id = groups[groupIndex][i];
                    world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(startingCoord);
                }
            }

            foreach (CharId id in group)
            {
                string inputSymbol;
                bool usesPre;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputSymbol = ch.InputSymbol;
                    usesPre = ch.UsesInputPreexistingColors;
                }

                ColorPair finalColors = _characterFinalColorMap[id];
                string gradientScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    gradientScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                }

                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    Scene scene = ch.Animation.Scenes.Get(gradientScn)
                        ?? throw new EngineInvariantException("gradient scene");
                    if (dynamic)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            _config.FinalGradientFrames,
                            new VisualParams { Colors = finalColors });
                    }
                    else
                    {
                        Color finalFgColor = finalColors.FgColor
                            ?? throw new EngineInvariantException("gradient mapping fg");
                        Gradient charGradient = Gradient.WithSteps(
                            [_config.FinalGradientStops[0], finalFgColor],
                            10,
                            false);
                        scene.ApplyGradientToSymbols(
                            [inputSymbol],
                            _config.FinalGradientFrames,
                            charGradient,
                            null);
                    }
                }

                world.ActivateScene(this, id, gradientScn);
            }
        }

        _pendingGroups.Clear();
        foreach (List<CharId> g in groups)
        {
            _pendingGroups.Add(g);
        }

        _activeGroups.Clear();
        _currentGap = 0;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingGroups.Count > 0 || !world.ActiveCharacters.IsEmpty || _activeGroups.Count > 0)
        {
            if (_currentGap == _config.Gap && _pendingGroups.Count > 0)
            {
                // slide.rs:290 — remove(0) FIFO drain
                _activeGroups.Add(_pendingGroups[0]);
                _pendingGroups.RemoveAt(0);
                _currentGap = 0;
            }
            else if (_pendingGroups.Count > 0)
            {
                _currentGap += 1;
            }

            for (int groupIndex = 0; groupIndex < _activeGroups.Count; groupIndex++)
            {
                if (_activeGroups[groupIndex].Count > 0)
                {
                    // slide.rs:297 — remove(0) FIFO drain
                    CharId nextChar = _activeGroups[groupIndex][0];
                    _activeGroups[groupIndex].RemoveAt(0);
                    world.Terminal.SetCharacterVisibility(nextChar, true);
                    world.ActivatePath(this, nextChar, "input_path");
                    world.ActiveCharacters.Insert(
                        nextChar,
                        world.Terminal.Arena[(int)nextChar.Value].CharacterId);
                }
            }

            _activeGroups.RemoveAll(group => group.Count == 0);
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
