using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>PourIterator.PourDirection.</summary>
public enum PourDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>pour, ported from effects/effect_pour.py. Transcribed from <c>effects/pour.rs</c>.</summary>
public sealed class PourConfig
{
    public PourDirection PourDirection { get; set; } = PourDirection.Down;
    public long PourSpeed { get; set; } = 2;
    public (double Min, double Max) MovementSpeedRange { get; set; } = (0.4, 0.6);
    public long Gap { get; set; } = 1;
    public Color StartingColor { get; set; } = Color.FromHex("ffffff");
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 6;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
    public Easing MovementEasing { get; set; } = Easing.InQuad;
}

public sealed class Pour : IEffect
{
    private readonly PourConfig _config;
    private readonly List<List<CharId>> _pendingGroups;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private List<CharId> _currentGroup;
    private long _gap;

    public Pour(PourConfig config)
    {
        _config = config;
        _pendingGroups = new List<List<CharId>>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _currentGroup = new List<CharId>();
        _gap = 0;
    }

    /// <summary>pour.rs parse_pour_direction.</summary>
    public static object ParsePourDirection(string s)
    {
        return s switch
        {
            "up" => PourDirection.Up,
            "down" => PourDirection.Down,
            "left" => PourDirection.Left,
            "right" => PourDirection.Right,
            _ => throw new UsageError($"invalid choice: '{s}' (choose from 'up', 'down', 'left', 'right')"),
        };
    }

    public static Pour FromOptions(Dictionary<string, object> options)
    {
        (double min, double max) = ((double, double))options["--movement-speed-range"];
        return new Pour(new PourConfig
        {
            PourDirection = (PourDirection)options["--pour-direction"],
            PourSpeed = (long)options["--pour-speed"],
            MovementSpeedRange = (min, max),
            Gap = (long)options["--gap"],
            StartingColor = (Color)options["--starting-color"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientFrames = (long)options["--final-gradient-frames"],
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
            MovementEasing = (Easing)options["--movement-easing"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    public void Build(EngineWorld world)
    {
        PourDirection pourDirection = _config.PourDirection;
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

        CharacterGroup grouping = pourDirection switch
        {
            PourDirection.Down => CharacterGroup.RowBottomToTop,
            PourDirection.Up => CharacterGroup.RowTopToBottom,
            PourDirection.Left => CharacterGroup.ColumnLeftToRight,
            PourDirection.Right => CharacterGroup.ColumnRightToLeft,
            _ => throw new EngineInvariantException("pour direction"),
        };
        List<List<CharId>> groups = world.Terminal.GetCharactersGrouped(CharacterFilter.Default, grouping);
        for (int i = 0; i < groups.Count; i++)
        {
            List<CharId> group = groups[i];
            foreach (CharId id in group)
            {
                world.Terminal.SetCharacterVisibility(id, false);
                Coord inputCoord;
                string inputSymbol;
                bool usesPre;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputCoord = ch.InputCoord;
                    inputSymbol = ch.InputSymbol;
                    usesPre = ch.UsesInputPreexistingColors;
                }

                Coord startCoord = pourDirection switch
                {
                    PourDirection.Down => Coord.New(inputCoord.Column, world.Terminal.Canvas.Top),
                    PourDirection.Up => Coord.New(inputCoord.Column, world.Terminal.Canvas.Bottom),
                    PourDirection.Left => Coord.New(world.Terminal.Canvas.Right, inputCoord.Row),
                    PourDirection.Right => Coord.New(world.Terminal.Canvas.Left, inputCoord.Row),
                    _ => throw new EngineInvariantException("pour direction"),
                };
                world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(startCoord);
                double speed = world.Rng.Uniform(
                    _config.MovementSpeedRange.Min,
                    _config.MovementSpeedRange.Max);
                string inputCoordPath;
                {
                    Motion motion = world.Terminal.Arena[(int)id.Value].Motion;
                    string pathId = motion.NewPath(
                        speed,
                        _config.MovementEasing,
                        null,
                        0,
                        false,
                        "");
                    Path path = motion.Paths.Get(pathId)
                        ?? throw new EngineInvariantException("input coord path");
                    path.NewWaypoint(inputCoord, null, "");
                    inputCoordPath = pathId;
                }

                world.ActivatePath(this, id, inputCoordPath);

                string pourScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    pourScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                }

                ColorPair finalColors = _characterFinalColorMap[id];
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    Scene scene = ch.Animation.Scenes.Get(pourScn)
                        ?? throw new EngineInvariantException("pour scene");
                    if (dynamic)
                    {
                        Color? finalFgColor = finalColors.FgColor;
                        Color? finalBgColor = finalColors.BgColor;
                        Gradient? fgGradient = finalFgColor is Color fg
                            ? Gradient.WithSteps([_config.StartingColor, fg], 10, false)
                            : null;
                        Gradient? bgGradient = finalBgColor is Color bg
                            ? Gradient.WithSteps([_config.StartingColor, bg], 10, false)
                            : null;
                        if (fgGradient is not null || bgGradient is not null)
                        {
                            scene.ApplyGradientToSymbols(
                                [inputSymbol],
                                _config.FinalGradientFrames,
                                fgGradient,
                                bgGradient);
                        }
                        else
                        {
                            scene.AddFrame(
                                inputSymbol,
                                _config.FinalGradientFrames,
                                new VisualParams { Colors = ColorPair.New(null, null) });
                        }
                    }
                    else
                    {
                        Color finalFgColor = finalColors.FgColor
                            ?? throw new EngineInvariantException("gradient mapping fg");
                        Gradient pourGradient = Gradient.New(
                            [_config.StartingColor, finalFgColor],
                            _config.FinalGradientSteps,
                            false,
                            false);
                        scene.ApplyGradientToSymbols(
                            [inputSymbol],
                            _config.FinalGradientFrames,
                            pourGradient,
                            null);
                    }
                }

                world.ActivateScene(this, id, pourScn);
            }

            if (i % 2 == 0)
            {
                _pendingGroups.Add(new List<CharId>(group));
            }
            else
            {
                List<CharId> reversed = new List<CharId>(group);
                reversed.Reverse();
                _pendingGroups.Add(reversed);
            }
        }

        _gap = 0;
        // pour.rs:251 — remove(0) FIFO drain
        _currentGroup = _pendingGroups[0];
        _pendingGroups.RemoveAt(0);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingGroups.Count > 0 || !world.ActiveCharacters.IsEmpty || _currentGroup.Count > 0)
        {
            if (_currentGroup.Count == 0 && _pendingGroups.Count > 0)
            {
                // pour.rs:258 — remove(0) FIFO drain
                _currentGroup = _pendingGroups[0];
                _pendingGroups.RemoveAt(0);
            }

            if (_currentGroup.Count > 0)
            {
                if (_gap == 0)
                {
                    for (long i = 0; i < _config.PourSpeed; i++)
                    {
                        if (_currentGroup.Count > 0)
                        {
                            // pour.rs:264 — remove(0) FIFO drain
                            CharId nextCharacter = _currentGroup[0];
                            _currentGroup.RemoveAt(0);
                            world.Terminal.SetCharacterVisibility(nextCharacter, true);
                            world.ActiveCharacters.Insert(
                                nextCharacter,
                                world.Terminal.Arena[(int)nextCharacter.Value].CharacterId);
                        }
                    }

                    _gap = _config.Gap;
                }
                else
                {
                    _gap -= 1;
                }
            }

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
