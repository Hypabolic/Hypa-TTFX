using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>swarm, ported from effects/effect_swarm.py. Transcribed from <c>effects/swarm.rs</c>.</summary>
public sealed class SwarmConfig
{
    public List<Color> BaseColor { get; set; } = new List<Color>();
    public Color FlashColor { get; set; } = Color.FromHex("f2ea79");
    public double SwarmSize { get; set; } = 0.1;
    public double SwarmCoordination { get; set; } = 0.80;
    public (long Lower, long Upper) SwarmAreaCountRange { get; set; } = (2, 4);
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Horizontal;
}

public sealed class Swarm : IEffect
{
    private static readonly Color DynamicClearColor = Color.FromHex("#FFFFFF");

    private readonly SwarmConfig _config;
    private readonly List<List<CharId>> _swarms;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private bool _callNext;
    private string _activeSwarmArea;
    private List<CharId> _currentSwarm;

    public Swarm(SwarmConfig config)
    {
        _config = config;
        _swarms = new List<List<CharId>>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _callNext = true;
        _activeSwarmArea = "0_swarm_area";
        _currentSwarm = new List<CharId>();
    }

    public static Swarm FromOptions(Dictionary<string, object> options)
    {
        (long lower, long upper) areaRange = ((long, long))options["--swarm-area-count-range"];
        return new Swarm(new SwarmConfig
        {
            BaseColor = TypedList<Color>(options, "--base-color"),
            FlashColor = (Color)options["--flash-color"],
            SwarmSize = (double)options["--swarm-size"],
            SwarmCoordination = (double)options["--swarm-coordination"],
            SwarmAreaCountRange = areaRange,
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>SwarmIterator.make_swarms.</summary>
    private void MakeSwarms(EngineWorld world, long swarmSize)
    {
        List<CharId> unswarmedCharacters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.BottomToTopRightToLeft);

        while (unswarmedCharacters.Count > 0)
        {
            var newSwarm = new List<CharId>();
            for (long i = 0; i < swarmSize; i++)
            {
                if (unswarmedCharacters.Count == 0)
                {
                    break;
                }

                CharId id = unswarmedCharacters[^1];
                unswarmedCharacters.RemoveAt(unswarmedCharacters.Count - 1);
                newSwarm.Add(id);
            }

            _swarms.Add(newSwarm);
        }

        if (_swarms.Count == 0)
        {
            throw new EngineInvariantException("make_swarms: no swarms");
        }

        List<CharId> finalSwarm = _swarms[^1];
        _swarms.RemoveAt(_swarms.Count - 1);
        if (finalSwarm.Count < PyCompat.FloorDiv(swarmSize, 2))
        {
            if (_swarms.Count == 0)
            {
                throw new EngineInvariantException("upstream IndexError: no preceding swarm to merge into");
            }

            _swarms[^1].AddRange(finalSwarm);
        }
        else
        {
            _swarms.Add(finalSwarm);
        }
    }

    /// <summary>int(s[0]) on a path id string (effect_swarm.py's first-character parse).</summary>
    private static long FirstCharDigit(string s)
    {
        if (s.Length == 0 || s[0] < '0' || s[0] > '9')
        {
            throw new EngineInvariantException("path id must start with a digit");
        }

        return s[0] - '0';
    }

    public void Build(EngineWorld world)
    {
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        long swarmSize = Math.Max(PyCompat.RoundHalfEven(characters.Count * _config.SwarmSize), 1);
        MakeSwarms(world, swarmSize);

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
        foreach (CharId id in characters)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ColorPair finalColors = dynamic
                ? ColorPair.New(ch.Animation.InputFgColor, ch.Animation.InputBgColor)
                : ColorPair.New(
                    finalGradientMapping.Get(ch.InputCoord)
                        ?? throw new EngineInvariantException("gradient mapping missing"),
                    null);
            _characterFinalColorMap[id] = finalColors;
        }

        var flashList = new List<Color>(10);
        for (int i = 0; i < 10; i++)
        {
            flashList.Add(_config.FlashColor);
        }

        long canvasRight = world.Terminal.Canvas.Right;
        long canvasTop = world.Terminal.Canvas.Top;
        var circleCache = new Dictionary<Coord, List<Coord>>();

        for (int swarmIndex = 0; swarmIndex < _swarms.Count; swarmIndex++)
        {
            List<CharId> swarm = _swarms[swarmIndex];
            Color baseColor = world.Rng.Choice(_config.BaseColor);
            Gradient swarmGradient = Gradient.WithSteps([baseColor, _config.FlashColor], 7, false);
            var swarmGradientMirror = new List<Color>();
            swarmGradientMirror.AddRange(swarmGradient.Spectrum);
            swarmGradientMirror.AddRange(flashList);
            for (int i = swarmGradient.Spectrum.Count - 1; i >= 0; i--)
            {
                swarmGradientMirror.Add(swarmGradient.Spectrum[i]);
            }

            var swarmAreaCoordinateMap = new List<(Coord Key, List<Coord> Coords)>();
            Coord swarmSpawn = world.Terminal.Canvas.RandomCoord(world.Rng, true, false);
            var swarmAreas = new List<Coord>();
            long swarmAreaCount = world.Rng.Randint(
                _config.SwarmAreaCountRange.Lower,
                _config.SwarmAreaCountRange.Upper);
            Coord lastFocusCoord = swarmSpawn;
            long radius = Math.Max(PyCompat.FloorDiv(Math.Min(canvasRight, canvasTop), 2), 1);

            while (swarmAreas.Count < swarmAreaCount)
            {
                if (!circleCache.TryGetValue(lastFocusCoord, out List<Coord>? cached))
                {
                    cached = Geometry.FindCoordsOnCircle(lastFocusCoord, radius, 0, true);
                    circleCache[lastFocusCoord] = cached;
                }

                world.Rng.Shuffle(cached);
                var potentialFocusCoords = new List<Coord>(cached);
                Coord? nextFocusCoord = null;
                foreach (Coord coord in potentialFocusCoords)
                {
                    if (world.Terminal.Canvas.CoordIsInCanvas(coord))
                    {
                        nextFocusCoord = coord;
                        break;
                    }
                }

                Coord resolvedFocus = nextFocusCoord
                    ?? world.Terminal.Canvas.RandomCoord(world.Rng, false, false);
                swarmAreas.Add(resolvedFocus);
                List<Coord> areaCoords = Geometry.FindCoordsInCircle(
                    lastFocusCoord,
                    Math.Max(PyCompat.FloorDiv(Math.Min(canvasRight, canvasTop), 6), 1) * 2);

                bool found = false;
                for (int j = 0; j < swarmAreaCoordinateMap.Count; j++)
                {
                    if (swarmAreaCoordinateMap[j].Key.Equals(lastFocusCoord))
                    {
                        swarmAreaCoordinateMap[j] = (lastFocusCoord, areaCoords);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    swarmAreaCoordinateMap.Add((lastFocusCoord, areaCoords));
                }

                lastFocusCoord = resolvedFocus;
            }

            foreach (CharId id in swarm)
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

                string flashScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    ch.Motion.SetCoordinate(swarmSpawn);
                    flashScn = ch.Animation.NewScene(false, SyncMetric.Distance, null, "", usesPre);
                    Scene scene = ch.Animation.Scenes.Get(flashScn)
                        ?? throw new EngineInvariantException("flash scene");
                    foreach (Color step in swarmGradientMirror)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            1,
                            new VisualParams { Colors = ColorPair.New(step, null) });
                    }
                }

                for (int swarmAreaCountIdx = 0; swarmAreaCountIdx < swarmAreaCoordinateMap.Count; swarmAreaCountIdx++)
                {
                    List<Coord> swarmAreaCoords = swarmAreaCoordinateMap[swarmAreaCountIdx].Coords;
                    string swarmAreaName = $"{swarmAreaCountIdx}_swarm_area";
                    Coord originWaypointCoord = world.Rng.Choice(swarmAreaCoords);
                    {
                        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                        string originPath = ch.Motion.NewPath(0.4, Easing.OutSine, null, 0, false, swarmAreaName);
                        ch.Motion.Paths.Get(originPath)!
                            .NewWaypoint(originWaypointCoord, null, swarmAreaName);
                    }

                    world.RegisterEvent(
                        id,
                        Event.PathActivated,
                        new CallerKey.Path(swarmAreaName),
                        new EventAction.ActivateScene(flashScn));
                    world.RegisterEvent(
                        id,
                        Event.PathActivated,
                        new CallerKey.Path(swarmAreaName),
                        new EventAction.SetLayer(1));
                    world.RegisterEvent(
                        id,
                        Event.PathComplete,
                        new CallerKey.Path(swarmAreaName),
                        new EventAction.DeactivateScene(null));

                    long innerPaths = 0;
                    const long totalInnerPaths = 2;
                    while (innerPaths < totalInnerPaths)
                    {
                        Coord nextCoord = world.Rng.Choice(swarmAreaCoords);
                        innerPaths += 1;
                        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                        string innerPathId = ch.Motion.Paths.Count.ToString();
                        string innerPath = ch.Motion.NewPath(0.18, Easing.InOutSine, null, 0, false, innerPathId);
                        string waypointId = ch.Motion.Paths.Count.ToString();
                        ch.Motion.Paths.Get(innerPath)!
                            .NewWaypoint(nextCoord, null, waypointId);
                    }
                }

                string inputPath;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputPath = ch.Motion.NewPath(0.45, Easing.InOutQuad, null, 0, false, "");
                    ch.Motion.Paths.Get(inputPath)!
                        .NewWaypoint(inputCoord, null, "");
                }

                string inputScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                }

                ColorPair finalColors = _characterFinalColorMap[id];
                if (dynamic)
                {
                    if (finalColors.FgColor is null && finalColors.BgColor is null)
                    {
                        Gradient clearGradient = Gradient.WithSteps(
                            [_config.FlashColor, DynamicClearColor],
                            10,
                            false);
                        Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(inputScn)
                            ?? throw new EngineInvariantException("input scene");
                        foreach (Color step in clearGradient.Spectrum)
                        {
                            scene.AddFrame(
                                inputSymbol,
                                3,
                                new VisualParams { Colors = ColorPair.New(step, null) });
                        }

                        scene.AddFrame(
                            inputSymbol,
                            3,
                            new VisualParams { Colors = new ColorPair() });
                    }
                    else
                    {
                        Gradient? fgGradient = finalColors.FgColor is not null
                            ? Gradient.WithSteps([_config.FlashColor, finalColors.FgColor], 10, false)
                            : null;
                        Gradient? bgGradient = finalColors.BgColor is not null
                            ? Gradient.WithSteps([_config.FlashColor, finalColors.BgColor], 10, false)
                            : null;
                        world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(inputScn)!
                            .ApplyGradientToSymbols([inputSymbol], 3, fgGradient, bgGradient);
                    }
                }
                else
                {
                    Color finalFg = finalColors.FgColor
                        ?? throw new EngineInvariantException("gradient mapping fg");
                    Gradient landingGradient = Gradient.WithSteps([_config.FlashColor, finalFg], 10, false);
                    Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(inputScn)
                        ?? throw new EngineInvariantException("input scene");
                    foreach (Color step in landingGradient.Spectrum)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            3,
                            new VisualParams { Colors = ColorPair.New(step, null) });
                    }
                }

                world.RegisterEvent(
                    id,
                    Event.PathComplete,
                    new CallerKey.Path(inputPath),
                    new EventAction.ActivateScene(inputScn));
                world.RegisterEvent(
                    id,
                    Event.PathComplete,
                    new CallerKey.Path(inputPath),
                    new EventAction.SetLayer(0));
                world.RegisterEvent(
                    id,
                    Event.PathActivated,
                    new CallerKey.Path(inputPath),
                    new EventAction.ActivateScene(flashScn));

                var allPaths = new List<string>();
                foreach (string key in world.Terminal.Arena[(int)id.Value].Motion.Paths.Keys)
                {
                    allPaths.Add(key);
                }

                world.ChainPaths(id, allPaths, false);
            }
        }

        _callNext = true;
        _activeSwarmArea = "0_swarm_area";
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_swarms.Count > 0 || !world.ActiveCharacters.IsEmpty)
        {
            if (_swarms.Count > 0 && _callNext)
            {
                _callNext = false;
                _currentSwarm = _swarms[^1];
                _swarms.RemoveAt(_swarms.Count - 1);
                _activeSwarmArea = "0_swarm_area";
                List<CharId> currentCopy = new List<CharId>(_currentSwarm);
                foreach (CharId id in currentCopy)
                {
                    world.ActivatePath(this, id, "0_swarm_area");
                    world.Terminal.SetCharacterVisibility(id, true);
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                }
            }

            if (world.ActiveCharacters.Count < _currentSwarm.Count)
            {
                _callNext = true;
            }

            if (_currentSwarm.Count > 0)
            {
                for (int i = 0; i < _currentSwarm.Count; i++)
                {
                    CharId id = _currentSwarm[i];
                    string? activePathId = world.Terminal.Arena[(int)id.Value].Motion.ActivePath;
                    if (activePathId is not null
                        && activePathId != _activeSwarmArea
                        && activePathId.Contains("swarm_area", StringComparison.Ordinal)
                        && FirstCharDigit(activePathId) > FirstCharDigit(_activeSwarmArea))
                    {
                        _activeSwarmArea = activePathId;
                        List<CharId> currentCopy = new List<CharId>(_currentSwarm);
                        foreach (CharId other in currentCopy)
                        {
                            if (!other.Equals(id) && world.Rng.Random() < _config.SwarmCoordination)
                            {
                                world.ActivatePath(this, other, _activeSwarmArea);
                            }
                        }

                        break;
                    }
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
