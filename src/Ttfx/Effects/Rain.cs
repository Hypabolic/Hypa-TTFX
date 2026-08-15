using System.Collections.Generic;
using System.Linq;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>rain, ported from effects/effect_rain.py. Transcribed from <c>effects/rain.rs</c>.</summary>
public sealed class RainConfig
{
    public List<Color> RainColors { get; set; } = new List<Color>();
    public (double Min, double Max) MovementSpeed { get; set; } = (0.33, 0.57);
    public List<string> RainSymbols { get; set; } = new List<string>();
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
    public Easing MovementEasing { get; set; } = Easing.InQuart;
}

public sealed class Rain : IEffect
{
    private readonly RainConfig _config;
    private readonly List<CharId> _pendingChars;
    // BTreeMap in rain.rs — SortedDictionary min-key iteration matches.
    private readonly SortedDictionary<long, List<CharId>> _groupByRow;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;

    public Rain(RainConfig config)
    {
        _config = config;
        _pendingChars = new List<CharId>();
        _groupByRow = new SortedDictionary<long, List<CharId>>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    }

    public static Rain FromOptions(Dictionary<string, object> options)
    {
        (double min, double max) = ((double, double))options["--movement-speed"];
        return new Rain(new RainConfig
        {
            RainColors = TypedList<Color>(options, "--rain-colors"),
            MovementSpeed = (min, max),
            RainSymbols = TypedList<string>(options, "--rain-symbols"),
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
            MovementEasing = (Easing)options["--movement-easing"],
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
            ColorPair finalColors;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
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
            }

            _characterFinalColorMap[id] = finalColors;
        }

        long canvasTop = world.Terminal.Canvas.Top;
        foreach (CharId id in characters)
        {
            Coord inputCoord;
            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
            }

            Color raindropColor = world.Rng.Choice(_config.RainColors);
            string rainScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                rainScn = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            string rainSymbol = world.Rng.Choice(_config.RainSymbols);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(rainScn)
                    ?? throw new EngineInvariantException("rain scene");
                scene.AddFrame(
                    rainSymbol,
                    1,
                    new VisualParams { Colors = ColorPair.New(raindropColor, null) });
            }

            string fadeScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                fadeScn = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            ColorPair finalColors = _characterFinalColorMap[id];
            if (dynamic)
            {
                Gradient? fgGradient = finalColors.FgColor is Color fg
                    ? Gradient.WithSteps([raindropColor, fg], 7, false)
                    : null;
                Gradient? bgGradient = finalColors.BgColor is Color bg
                    ? Gradient.WithSteps([raindropColor, bg], 7, false)
                    : null;
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(fadeScn)
                    ?? throw new EngineInvariantException("fade scene");
                if (fgGradient is not null || bgGradient is not null)
                {
                    scene.ApplyGradientToSymbols([inputSymbol], 3, fgGradient, bgGradient);
                }
                else
                {
                    scene.AddFrame(
                        inputSymbol,
                        3,
                        new VisualParams { Colors = ColorPair.New(null, null) });
                }
            }
            else
            {
                Color finalFg = finalColors.FgColor
                    ?? throw new EngineInvariantException("gradient mapping fg");
                Gradient raindropGradient = Gradient.WithSteps([raindropColor, finalFg], 7, false);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(fadeScn)
                    ?? throw new EngineInvariantException("fade scene");
                scene.ApplyGradientToSymbols([inputSymbol], 3, raindropGradient, null);
            }

            world.ActivateScene(this, id, rainScn);
            double speed = world.Rng.Uniform(_config.MovementSpeed.Min, _config.MovementSpeed.Max);
            string inputPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(Coord.New(inputCoord.Column, canvasTop));
                string pathId = ch.Motion.NewPath(
                    speed,
                    _config.MovementEasing,
                    null,
                    0,
                    false,
                    "");
                Path path = ch.Motion.Paths.Get(pathId)
                    ?? throw new EngineInvariantException("input path");
                path.NewWaypoint(inputCoord, null, "");
                inputPath = pathId;
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(inputPath),
                new EventAction.ActivateScene(fadeScn));
            world.ActivatePath(this, id, inputPath);
            _pendingChars.Add(id);
        }

        // rain.rs:219 — sort_by_key is stable; List.Sort is not
        List<CharId> sortedChars = _pendingChars
            .OrderBy(id => world.Terminal.Arena[(int)id.Value].InputCoord.Row)
            .ToList();
        foreach (CharId id in sortedChars)
        {
            long row = world.Terminal.Arena[(int)id.Value].InputCoord.Row;
            if (!_groupByRow.TryGetValue(row, out List<CharId>? group))
            {
                group = new List<CharId>();
                _groupByRow[row] = group;
            }

            group.Add(id);
        }

        _pendingChars.Clear();
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_groupByRow.Count > 0 || !world.ActiveCharacters.IsEmpty || _pendingChars.Count > 0)
        {
            if (_pendingChars.Count == 0 && _groupByRow.Count > 0)
            {
                long minRow = 0;
                foreach (long key in _groupByRow.Keys)
                {
                    minRow = key;
                    break;
                }

                if (!_groupByRow.Remove(minRow, out List<CharId>? group))
                {
                    throw new EngineInvariantException("group_by_row missing min row");
                }

                _pendingChars.AddRange(group);
            }

            if (_pendingChars.Count > 0)
            {
                long drops = world.Rng.Randint(1, 2);
                for (long i = 0; i < drops; i++)
                {
                    if (_pendingChars.Count == 0)
                    {
                        break;
                    }

                    // rain.rs:240-241 — Randint(0, pending.len()-1) then RemoveAt
                    int index = (int)world.Rng.Randint(0, _pendingChars.Count - 1);
                    CharId nextCharacter = _pendingChars[index];
                    _pendingChars.RemoveAt(index);
                    world.Terminal.SetCharacterVisibility(nextCharacter, true);
                    world.ActiveCharacters.Insert(
                        nextCharacter,
                        world.Terminal.Arena[(int)nextCharacter.Value].CharacterId);
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
