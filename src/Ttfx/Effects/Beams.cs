using System.Collections.Generic;
using System.Linq;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>BeamsIterator.Direction.</summary>
public enum BeamsDirection
{
    Row,
    Column,
}

/// <summary>BeamsIterator.Phase.</summary>
public enum BeamsPhase
{
    Beams,
    FinalWipe,
    Complete,
}

/// <summary>BeamsIterator.Group state (get_next_character lives on Beams for hooks access).</summary>
public sealed class BeamsGroup
{
    public List<CharId> Characters { get; set; } = new List<CharId>();
    public BeamsDirection Direction { get; set; }
    public double Speed { get; set; }
    public double NextCharacterCounter { get; set; }
}

/// <summary>beams, ported from effects/effect_beams.py. Transcribed from <c>effects/beams.rs</c>.</summary>
public sealed class BeamsConfig
{
    public List<string> BeamRowSymbols { get; set; } = new List<string>();
    public List<string> BeamColumnSymbols { get; set; } = new List<string>();
    public long BeamDelay { get; set; } = 6;
    public (long Min, long Max) BeamRowSpeedRange { get; set; } = (15, 60);
    public (long Min, long Max) BeamColumnSpeedRange { get; set; } = (9, 15);
    public List<Color> BeamGradientStops { get; set; } = new List<Color>();
    public List<long> BeamGradientSteps { get; set; } = new List<long>();
    public long BeamGradientFrames { get; set; } = 2;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 4;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
    public long FinalWipeSpeed { get; set; } = 3;
}

public sealed class Beams : IEffect
{
    private readonly BeamsConfig _config;
    private readonly List<BeamsGroup> _pendingGroups;
    private readonly List<BeamsGroup> _activeGroups;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private List<List<CharId>> _finalWipeGroups;
    private long _delay;
    private BeamsPhase _phase;

    public Beams(BeamsConfig config)
    {
        _config = config;
        _pendingGroups = new List<BeamsGroup>();
        _activeGroups = new List<BeamsGroup>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _finalWipeGroups = new List<List<CharId>>();
        _delay = 0;
        _phase = BeamsPhase.Beams;
    }

    public static Beams FromOptions(Dictionary<string, object> options)
    {
        (long rowMin, long rowMax) = ((long, long))options["--beam-row-speed-range"];
        (long colMin, long colMax) = ((long, long))options["--beam-column-speed-range"];
        return new Beams(new BeamsConfig
        {
            BeamRowSymbols = TypedList<string>(options, "--beam-row-symbols"),
            BeamColumnSymbols = TypedList<string>(options, "--beam-column-symbols"),
            BeamDelay = (long)options["--beam-delay"],
            BeamRowSpeedRange = (rowMin, rowMax),
            BeamColumnSpeedRange = (colMin, colMax),
            BeamGradientStops = TypedList<Color>(options, "--beam-gradient-stops"),
            BeamGradientSteps = TypedList<long>(options, "--beam-gradient-steps"),
            BeamGradientFrames = (long)options["--beam-gradient-frames"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientFrames = (long)options["--final-gradient-frames"],
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
            FinalWipeSpeed = (long)options["--final-wipe-speed"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>Group.__init__.</summary>
    private BeamsGroup MakeGroup(EngineWorld world, List<CharId> characters, BeamsDirection direction)
    {
        (long min, long max) = direction switch
        {
            BeamsDirection.Row => _config.BeamRowSpeedRange,
            BeamsDirection.Column => _config.BeamColumnSpeedRange,
            _ => throw new EngineInvariantException("beams direction"),
        };
        double speed = world.Rng.Randint(min, max) * 0.1;
        List<CharId> sorted;
        switch (direction)
        {
            case BeamsDirection.Row:
                // beams.rs:134 — sort_by_key is stable; List.Sort is not
                sorted = characters
                    .OrderBy(id => world.Terminal.Arena[(int)id.Value].InputCoord.Column)
                    .ToList();
                break;
            case BeamsDirection.Column:
                // beams.rs:137 — sort_by_key is stable; List.Sort is not
                sorted = characters
                    .OrderBy(id => world.Terminal.Arena[(int)id.Value].InputCoord.Row)
                    .ToList();
                break;
            default:
                throw new EngineInvariantException("beams direction");
        }

        if (world.Rng.Choice(new[] { true, false }))
        {
            sorted.Reverse();
        }

        return new BeamsGroup
        {
            Characters = sorted,
            Direction = direction,
            Speed = speed,
            NextCharacterCounter = 0.0,
        };
    }

    /// <summary>Group.get_next_character.</summary>
    private CharId? GetNextCharacter(EngineWorld world, BeamsGroup group)
    {
        group.NextCharacterCounter -= 1.0;
        // beams.rs:149 — FIFO remove(0)
        CharId nextCharacter = group.Characters[0];
        group.Characters.RemoveAt(0);
        string? activeScene = world.Terminal.Arena[(int)nextCharacter.Value].Animation.ActiveScene;
        CharId? returnValue;
        if (activeScene is not null)
        {
            world.Terminal.Arena[(int)nextCharacter.Value].Animation.Scenes.Get(activeScene)!
                .ResetScene();
            returnValue = null;
        }
        else
        {
            world.Terminal.SetCharacterVisibility(nextCharacter, true);
            returnValue = nextCharacter;
        }

        string sceneName = group.Direction switch
        {
            BeamsDirection.Row => "beam_row",
            BeamsDirection.Column => "beam_column",
            _ => throw new EngineInvariantException("beams direction"),
        };
        world.ActivateScene(this, nextCharacter, sceneName);
        return returnValue;
    }

    public void Build(EngineWorld world)
    {
        // __init__ precomputation (no RNG)
        _finalWipeGroups = world.Terminal.GetCharactersGrouped(
            CharacterFilter.Default,
            CharacterGroup.DiagonalTopLeftToBottomRight);

        CharacterFilter allCharsFilter = new CharacterFilter(true, true, true, false);
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
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            allCharsFilter,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            if (ch.IsFillCharacter)
            {
                _characterFinalColorMap[id] = ColorPair.New(Color.FromHex("#000000"), null);
                continue;
            }

            ColorPair finalColors;
            if (dynamic)
            {
                finalColors = ColorPair.New(ch.Animation.InputFgColor, ch.Animation.InputBgColor);
            }
            else
            {
                Color mapped = finalGradientMapping.Get(ch.InputCoord)
                    ?? throw new EngineInvariantException("gradient mapping missing");
                finalColors = ColorPair.New(mapped, null);
            }

            _characterFinalColorMap[id] = finalColors;
        }

        Gradient beamGradient = Gradient.New(
            _config.BeamGradientStops,
            _config.BeamGradientSteps,
            false,
            false);
        var groups = new List<BeamsGroup>();
        foreach (List<CharId> row in world.Terminal.GetCharactersGrouped(
                     allCharsFilter,
                     CharacterGroup.RowTopToBottom))
        {
            groups.Add(MakeGroup(world, row, BeamsDirection.Row));
        }

        foreach (List<CharId> column in world.Terminal.GetCharactersGrouped(
                     allCharsFilter,
                     CharacterGroup.ColumnLeftToRight))
        {
            groups.Add(MakeGroup(world, column, BeamsDirection.Column));
        }

        // Rows and columns contain the same characters; initialize each character's scenes only once.
        foreach (BeamsGroup group in groups.Where(g => g.Direction == BeamsDirection.Row))
        {
            foreach (CharId id in group.Characters)
            {
                string inputSymbol;
                bool usesPre;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputSymbol = ch.InputSymbol;
                    usesPre = ch.UsesInputPreexistingColors;
                }

                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    ch.Animation.NewScene(false, null, null, "beam_row", usesPre);
                    ch.Animation.NewScene(false, null, null, "beam_column", usesPre);
                    ch.Animation.NewScene(false, null, null, "brighten", usesPre);
                    ch.Animation.Scenes.Get("beam_row")!
                        .ApplyGradientToSymbols(
                            _config.BeamRowSymbols,
                            _config.BeamGradientFrames,
                            beamGradient,
                            null);
                    ch.Animation.Scenes.Get("beam_column")!
                        .ApplyGradientToSymbols(
                            _config.BeamColumnSymbols,
                            _config.BeamGradientFrames,
                            beamGradient,
                            null);
                }

                ColorPair charColors = _characterFinalColorMap[id];
                Gradient? fgFadeGradient = null;
                Gradient? bgFadeGradient = null;
                Gradient? fgBrightenGradient = null;
                Gradient? bgBrightenGradient = null;
                if (charColors.FgColor is Color fg)
                {
                    Color fadedFgColor = Animation.AdjustColorBrightness(fg, 0.3);
                    fgFadeGradient = Gradient.WithSteps([fg, fadedFgColor], 10, false);
                    fgBrightenGradient = Gradient.WithSteps([fadedFgColor, fg], 10, false);
                }

                if (charColors.BgColor is Color bg)
                {
                    Color fadedBgColor = Animation.AdjustColorBrightness(bg, 0.3);
                    bgFadeGradient = Gradient.WithSteps([bg, fadedBgColor], 10, false);
                    bgBrightenGradient = Gradient.WithSteps([fadedBgColor, bg], 10, false);
                }

                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    if (fgFadeGradient is not null || bgFadeGradient is not null)
                    {
                        ch.Animation.Scenes.Get("beam_row")!
                            .ApplyGradientToSymbols(
                                [inputSymbol],
                                2,
                                fgFadeGradient,
                                bgFadeGradient);
                        ch.Animation.Scenes.Get("beam_column")!
                            .ApplyGradientToSymbols(
                                [inputSymbol],
                                2,
                                fgFadeGradient,
                                bgFadeGradient);
                    }
                    else
                    {
                        ch.Animation.Scenes.Get("beam_row")!
                            .AddFrame(
                                inputSymbol,
                                2,
                                new VisualParams { Colors = ColorPair.New(null, null) });
                        ch.Animation.Scenes.Get("beam_column")!
                            .AddFrame(
                                inputSymbol,
                                2,
                                new VisualParams { Colors = ColorPair.New(null, null) });
                    }

                    if (fgBrightenGradient is not null || bgBrightenGradient is not null)
                    {
                        ch.Animation.Scenes.Get("brighten")!
                            .ApplyGradientToSymbols(
                                [inputSymbol],
                                _config.FinalGradientFrames,
                                fgBrightenGradient,
                                bgBrightenGradient);
                    }
                    else
                    {
                        ch.Animation.Scenes.Get("brighten")!
                            .AddFrame(
                                inputSymbol,
                                _config.FinalGradientFrames,
                                new VisualParams { Colors = ColorPair.New(null, null) });
                    }
                }
            }
        }

        _pendingGroups.Clear();
        _pendingGroups.AddRange(groups);
        world.Rng.Shuffle(_pendingGroups);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_phase != BeamsPhase.Complete || !world.ActiveCharacters.IsEmpty)
        {
            switch (_phase)
            {
                case BeamsPhase.Beams:
                    if (_delay == 0)
                    {
                        if (_pendingGroups.Count > 0)
                        {
                            long batch = world.Rng.Randint(1, 5);
                            for (long i = 0; i < batch; i++)
                            {
                                if (_pendingGroups.Count > 0)
                                {
                                    // beams.rs:379 — FIFO remove(0); outer for still runs remaining iterations
                                    BeamsGroup next = _pendingGroups[0];
                                    _pendingGroups.RemoveAt(0);
                                    _activeGroups.Add(next);
                                }
                            }
                        }

                        _delay = _config.BeamDelay;
                    }
                    else
                    {
                        _delay -= 1;
                    }

                    var activeGroups = new List<BeamsGroup>(_activeGroups);
                    _activeGroups.Clear();
                    foreach (BeamsGroup group in activeGroups)
                    {
                        group.NextCharacterCounter += group.Speed;
                        // beams.rs:391 — int() truncation
                        long count = PyCompat.TruncToI64(group.NextCharacterCounter);
                        if (count > 1)
                        {
                            for (long i = 0; i < count; i++)
                            {
                                if (group.Characters.Count > 0)
                                {
                                    CharId? nextChar = GetNextCharacter(world, group);
                                    if (nextChar is CharId cid)
                                    {
                                        world.ActiveCharacters.Insert(
                                            cid,
                                            world.Terminal.Arena[(int)cid.Value].CharacterId);
                                    }
                                }
                            }
                        }
                    }

                    foreach (BeamsGroup group in activeGroups)
                    {
                        if (group.Characters.Count > 0)
                        {
                            _activeGroups.Add(group);
                        }
                    }

                    if (_pendingGroups.Count == 0
                        && _activeGroups.Count == 0
                        && world.ActiveCharacters.IsEmpty)
                    {
                        _phase = BeamsPhase.FinalWipe;
                    }

                    break;
                case BeamsPhase.FinalWipe:
                    if (_finalWipeGroups.Count > 0)
                    {
                        for (long i = 0; i < _config.FinalWipeSpeed; i++)
                        {
                            if (_finalWipeGroups.Count == 0)
                            {
                                break;
                            }

                            // beams.rs:417 — FIFO remove(0)
                            List<CharId> nextGroup = _finalWipeGroups[0];
                            _finalWipeGroups.RemoveAt(0);
                            foreach (CharId id in nextGroup)
                            {
                                world.ActivateScene(this, id, "brighten");
                                world.Terminal.SetCharacterVisibility(id, true);
                                world.ActiveCharacters.Insert(
                                    id,
                                    world.Terminal.Arena[(int)id.Value].CharacterId);
                            }
                        }
                    }
                    else
                    {
                        _phase = BeamsPhase.Complete;
                    }

                    break;
                case BeamsPhase.Complete:
                    break;
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
