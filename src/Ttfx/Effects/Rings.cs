using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>rings, ported from effects/effect_rings.py. Transcribed from <c>effects/rings.rs</c>.</summary>
public sealed class RingsConfig
{
    public List<Color> RingColors { get; set; } = new List<Color>();
    public double RingGap { get; set; } = 0.1;
    public long SpinDuration { get; set; } = 200;
    public (double Lower, double Upper) SpinSpeed { get; set; } = (0.25, 1.0);
    public long DisperseDuration { get; set; } = 200;
    public long SpinDisperseCycles { get; set; } = 3;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

internal enum RingsPhase
{
    Start,
    Disperse,
    Spin,
    Final,
    Complete,
}

/// <summary>RingsIterator.Ring.</summary>
internal sealed class Ring
{
    public long Radius { get; }
    public Coord Origin { get; }
    public List<Coord> CounterClockwiseCoords { get; }
    public List<Coord> ClockwiseCoords { get; }
    public long RingGap { get; }
    public Color RingColor { get; }
    public List<CharId> Characters { get; } = new List<CharId>();
    public Dictionary<CharId, string> CharacterLastRingPath { get; } = new Dictionary<CharId, string>();
    public double RotationSpeed { get; }

    public Ring(
        EngineWorld world,
        RingsConfig config,
        long radius,
        Coord origin,
        List<Coord> ringCoords,
        long ringGap,
        Color ringColor)
    {
        Radius = radius;
        Origin = origin;
        CounterClockwiseCoords = ringCoords;
        ClockwiseCoords = new List<Coord>(ringCoords);
        ClockwiseCoords.Reverse();
        RingGap = ringGap;
        RingColor = ringColor;
        RotationSpeed = world.Rng.Uniform(config.SpinSpeed.Lower, config.SpinSpeed.Upper);
    }

    /// <summary>Ring.make_disperse_waypoints.</summary>
    public string MakeDisperseWaypoints(EngineWorld world, CharId id, Coord originCoord)
    {
        List<Coord> disperseCoords = Geometry.FindCoordsInRect(originCoord, RingGap);
        var waypointCoords = new List<Coord>(5);
        for (int i = 0; i < 5; i++)
        {
            int index = (int)world.Rng.Randrange(0, disperseCoords.Count);
            waypointCoords.Add(disperseCoords[index]);
        }

        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        // rings.rs:117 — keyed remove from insertion-ordered paths map
        ch.Motion.Paths.Remove("disperse");
        string pathId = ch.Motion.NewPath(0.14, null, null, 0, true, "disperse");
        Path path = ch.Motion.Paths.Get(pathId)
            ?? throw new EngineInvariantException("disperse path");
        foreach (Coord coord in waypointCoords)
        {
            path.NewWaypoint(coord, null, "");
        }

        return pathId;
    }
}

public sealed class Rings : IEffect
{
    private const uint CbSetInvisible = 0;

    private readonly RingsConfig _config;
    private List<Ring> _rings;
    private readonly List<CharId> _nonRingChars;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private RingsPhase _phase;
    private bool _initialDisperseComplete;
    private long _spinTimeRemaining;
    private long _disperseTimeRemaining;
    private long _cyclesRemaining;
    private long _initialPhaseTimeRemaining;

    public Rings(RingsConfig config)
    {
        _config = config;
        _rings = new List<Ring>();
        _nonRingChars = new List<CharId>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _phase = RingsPhase.Start;
        _initialDisperseComplete = false;
        _spinTimeRemaining = config.SpinDuration;
        _disperseTimeRemaining = config.DisperseDuration;
        _cyclesRemaining = config.SpinDisperseCycles;
        _initialPhaseTimeRemaining = 100;
    }

    public static Rings FromOptions(Dictionary<string, object> options)
    {
        (double lower, double upper) spinSpeed = ((double, double))options["--spin-speed"];
        return new Rings(new RingsConfig
        {
            RingColors = TypedList<Color>(options, "--ring-colors"),
            RingGap = (double)options["--ring-gap"],
            SpinDuration = (long)options["--spin-duration"],
            SpinSpeed = spinSpeed,
            DisperseDuration = (long)options["--disperse-duration"],
            SpinDisperseCycles = (long)options["--spin-disperse-cycles"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
        if (callback.Id == CbSetInvisible)
        {
            world.Terminal.SetCharacterVisibility(character, false);
        }
    }

    private void RingAddCharacter(EngineWorld world, Ring ring, CharId id, bool clockwise)
    {
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        string inputSymbol;
        bool usesPre;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            inputSymbol = ch.InputSymbol;
            usesPre = ch.UsesInputPreexistingColors;
        }

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string gradientScn = ch.Animation.NewScene(false, null, null, "gradient", usesPre);
            Scene scene = ch.Animation.Scenes.Get(gradientScn)
                ?? throw new EngineInvariantException("gradient scene");
            if (dynamic)
            {
                ColorPair colors = _characterFinalColorMap[id];
                scene.AddFrame(inputSymbol, 1, new VisualParams { Colors = colors });
            }
            else
            {
                Color finalFgColor = _characterFinalColorMap[id].FgColor
                    ?? throw new EngineInvariantException("gradient mapping fg");
                Gradient charGradient = Gradient.WithSteps([finalFgColor, ring.RingColor], 8, false);
                scene.ApplyGradientToSymbols([inputSymbol], 3, charGradient, null);
            }
        }

        var ringPaths = new List<string>();
        int characterStartingIndex = ring.Characters.Count;
        List<Coord> coords = clockwise ? ring.ClockwiseCoords : ring.CounterClockwiseCoords;
        var rotated = new List<Coord>();
        for (int i = characterStartingIndex; i < coords.Count; i++)
        {
            rotated.Add(coords[i]);
        }

        for (int i = 0; i < characterStartingIndex; i++)
        {
            rotated.Add(coords[i]);
        }

        foreach (Coord coord in rotated)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string pathId = ch.Motion.NewPath(ring.RotationSpeed, null, null, 0, false, ringPaths.Count.ToString());
            Path path = ch.Motion.Paths.Get(pathId)
                ?? throw new EngineInvariantException("ring path");
            string waypointId = path.Waypoints.Count.ToString();
            path.NewWaypoint(coord, null, waypointId);
            ringPaths.Add(pathId);
        }

        ring.CharacterLastRingPath[id] = ringPaths[0];

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string disperseScn = ch.Animation.NewScene(false, null, null, "disperse", usesPre);
            Scene scene = ch.Animation.Scenes.Get(disperseScn)
                ?? throw new EngineInvariantException("disperse scene");
            if (dynamic)
            {
                ColorPair colors = _characterFinalColorMap[id];
                scene.AddFrame(inputSymbol, 1, new VisualParams { Colors = colors });
            }
            else
            {
                Color finalFgColor = _characterFinalColorMap[id].FgColor
                    ?? throw new EngineInvariantException("gradient mapping fg");
                Gradient disperseGradient = Gradient.WithSteps([ring.RingColor, finalFgColor], 8, false);
                scene.ApplyGradientToSymbols([inputSymbol], 10, disperseGradient, null);
            }
        }

        world.ChainPaths(id, ringPaths, true);
        ring.Characters.Add(id);
    }

    private void RingDisperse(EngineWorld world, Ring ring)
    {
        List<CharId> characters = new List<CharId>(ring.Characters);
        foreach (CharId id in characters)
        {
            string? activePath;
            Coord currentCoord;
            {
                Motion motion = world.Terminal.Arena[(int)id.Value].Motion;
                activePath = motion.ActivePath;
                currentCoord = motion.CurrentCoord;
            }

            string last = activePath ?? "0";
            ring.CharacterLastRingPath[id] = last;
            string dispersePath = ring.MakeDisperseWaypoints(world, id, currentCoord);
            world.ActivatePath(this, id, dispersePath);
            world.ActivateScene(this, id, "disperse");
        }
    }

    private void RingSpin(EngineWorld world, Ring ring)
    {
        List<CharId> characters = new List<CharId>(ring.Characters);
        foreach (CharId id in characters)
        {
            string lastRingPath = ring.CharacterLastRingPath[id];
            string condensePath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Coord firstWaypointCoord = ch.Motion.Paths.Get(lastRingPath)!
                    .Waypoints[0]
                    .Coord;
                condensePath = ch.Motion.NewPath(0.1, null, null, 0, false, "");
                ch.Motion.Paths.Get(condensePath)!
                    .NewWaypoint(firstWaypointCoord, null, "");
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(condensePath),
                new EventAction.ActivatePath(lastRingPath));
            world.ActivatePath(this, id, condensePath);
            world.ActivateScene(this, id, "gradient");
        }
    }

    public void Build(EngineWorld world)
    {
        long ringGap = Math.Max(
            PyCompat.RoundHalfEven(Math.Min(world.Terminal.Canvas.Top, world.Terminal.Canvas.Right) * _config.RingGap),
            1);

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
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);

        var pendingChars = new List<CharId>();
        foreach (CharId id in characters)
        {
            Coord inputCoord;
            string inputSymbol;
            Color? inputFg;
            Color? inputBg;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                usesPre = ch.UsesInputPreexistingColors;
            }

            ColorPair finalColors = dynamic
                ? ColorPair.New(inputFg, inputBg)
                : ColorPair.New(
                    finalGradientMapping.Get(inputCoord)
                        ?? throw new EngineInvariantException("gradient mapping missing"),
                    null);
            _characterFinalColorMap[id] = finalColors;

            string startScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                startScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                ch.Animation.Scenes.Get(startScn)!
                    .AddFrame(inputSymbol, 1, new VisualParams { Colors = finalColors });
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string homePath = ch.Motion.NewPath(0.8, Easing.OutQuad, null, 0, false, "home");
                ch.Motion.Paths.Get(homePath)!
                    .NewWaypoint(inputCoord, null, "");
            }

            world.ActivateScene(this, id, startScn);
            world.Terminal.SetCharacterVisibility(id, true);
            pendingChars.Add(id);
        }

        world.Rng.Shuffle(pendingChars);

        var rings = new List<Ring>();
        Coord center = world.Terminal.Canvas.Center;
        long radiusLimit = Math.Max(world.Terminal.Canvas.Right, world.Terminal.Canvas.Top);
        long radius = 1;
        while (radius < radiusLimit)
        {
            List<Coord> ringCoords = Geometry.FindCoordsOnCircle(center, radius, 7 * radius, true);
            int inCanvasCount = 0;
            foreach (Coord coord in ringCoords)
            {
                if (world.Terminal.Canvas.CoordIsInCanvas(coord))
                {
                    inCanvasCount += 1;
                }
            }

            if (inCanvasCount / (double)ringCoords.Count < 0.25)
            {
                break;
            }

            Color ringColor = _config.RingColors[rings.Count % _config.RingColors.Count];
            rings.Add(new Ring(world, _config, radius, center, ringCoords, ringGap, ringColor));
            radius += ringGap;
        }

        // rings.rs:423 — pending_iter.pop_front()
        var pendingQueue = new Ttfx.Utils.Queue<CharId>();
        foreach (CharId id in pendingChars)
        {
            pendingQueue.PushBack(id);
        }

        var ringChars = new HashSet<CharId>();
        for (int ringCount = 0; ringCount < rings.Count; ringCount++)
        {
            Ring ring = rings[ringCount];
            for (int i = 0; i < ring.CounterClockwiseCoords.Count; i++)
            {
                if (!pendingQueue.IsEmpty)
                {
                    CharId nextCharacter = pendingQueue.PopFront();
                    bool clockwise = ringCount % 2 == 1;
                    RingAddCharacter(world, ring, nextCharacter, clockwise);
                    ringChars.Add(nextCharacter);
                }
            }
        }

        _rings = rings;

        List<CharId> allCharacters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in allCharacters)
        {
            if (ringChars.Contains(id))
            {
                continue;
            }

            Coord externalCoord = world.Terminal.Canvas.RandomCoord(world.Rng, true, false);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string externalPath = ch.Motion.NewPath(0.8, Easing.OutSine, null, 0, false, "external");
                ch.Motion.Paths.Get(externalPath)!
                    .NewWaypoint(externalCoord, null, "");
            }

            _nonRingChars.Add(id);
            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path("external"),
                new EventAction.Callback(new EffectCallback(CbSetInvisible, [])));
        }

        _phase = RingsPhase.Start;
        _initialDisperseComplete = false;
        _spinTimeRemaining = _config.SpinDuration;
        _disperseTimeRemaining = _config.DisperseDuration;
        _cyclesRemaining = _config.SpinDisperseCycles;
        _initialPhaseTimeRemaining = 100;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_phase == RingsPhase.Complete)
        {
            return null;
        }

        switch (_phase)
        {
            case RingsPhase.Start:
                if (_initialPhaseTimeRemaining == 0)
                {
                    _phase = RingsPhase.Disperse;
                }
                else
                {
                    _initialPhaseTimeRemaining -= 1;
                }

                break;

            case RingsPhase.Disperse:
                if (!_initialDisperseComplete)
                {
                    _initialDisperseComplete = true;
                    List<Ring> rings = _rings;
                    _rings = new List<Ring>();
                    foreach (Ring ring in rings)
                    {
                        List<CharId> characters = new List<CharId>(ring.Characters);
                        foreach (CharId id in characters)
                        {
                            Coord ringStartCoord = world.Terminal.Arena[(int)id.Value].Motion.Paths.Get("0")!
                                .Waypoints[0]
                                .Coord;
                            string dispersePath = ring.MakeDisperseWaypoints(world, id, ringStartCoord);
                            string initialPath;
                            {
                                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                                Coord disperseFirstCoord = ch.Motion.Paths.Get(dispersePath)!
                                    .Waypoints[0]
                                    .Coord;
                                initialPath = ch.Motion.NewPath(0.3, Easing.OutCubic, null, 0, false, "");
                                ch.Motion.Paths.Get(initialPath)!
                                    .NewWaypoint(disperseFirstCoord, null, "");
                            }

                            world.RegisterEvent(
                                id,
                                Event.PathComplete,
                                new CallerKey.Path(initialPath),
                                new EventAction.ActivatePath(dispersePath));
                            world.ActivateScene(this, id, "disperse");
                            world.ActivatePath(this, id, initialPath);
                            world.ActiveCharacters.Insert(
                                id,
                                world.Terminal.Arena[(int)id.Value].CharacterId);
                        }
                    }

                    _rings = rings;

                    List<CharId> nonRingChars = new List<CharId>(_nonRingChars);
                    foreach (CharId id in nonRingChars)
                    {
                        world.ActivatePath(this, id, "external");
                        world.ActiveCharacters.Insert(
                            id,
                            world.Terminal.Arena[(int)id.Value].CharacterId);
                    }
                }
                else if (_disperseTimeRemaining == 0)
                {
                    _phase = RingsPhase.Spin;
                    _cyclesRemaining -= 1;
                    _spinTimeRemaining = _config.SpinDuration;
                    List<Ring> rings = _rings;
                    _rings = new List<Ring>();
                    foreach (Ring ring in rings)
                    {
                        RingSpin(world, ring);
                    }

                    _rings = rings;
                }
                else
                {
                    _disperseTimeRemaining -= 1;
                }

                break;

            case RingsPhase.Spin:
                if (_spinTimeRemaining == 0)
                {
                    if (_cyclesRemaining == 0)
                    {
                        _phase = RingsPhase.Final;
                        List<CharId> characters = world.Terminal.GetCharacters(
                            world.Rng,
                            CharacterFilter.Default,
                            CharacterSort.TopToBottomLeftToRight);
                        foreach (CharId id in characters)
                        {
                            world.Terminal.SetCharacterVisibility(id, true);
                            world.ActivatePath(this, id, "home");
                            world.ActiveCharacters.Insert(
                                id,
                                world.Terminal.Arena[(int)id.Value].CharacterId);
                            if (world.Terminal.Arena[(int)id.Value].Motion.Paths.ContainsKey("external"))
                            {
                                continue;
                            }

                            world.ActivateScene(this, id, "disperse");
                        }
                    }
                    else
                    {
                        _disperseTimeRemaining = _config.DisperseDuration;
                        List<Ring> rings = _rings;
                        _rings = new List<Ring>();
                        foreach (Ring ring in rings)
                        {
                            RingDisperse(world, ring);
                        }

                        _rings = rings;
                        _phase = RingsPhase.Disperse;
                    }
                }
                else
                {
                    _spinTimeRemaining -= 1;
                }

                break;

            case RingsPhase.Final:
                if (world.ActiveCharacters.IsEmpty)
                {
                    _phase = RingsPhase.Complete;
                }

                break;
        }

        world.Update(this);
        return world.Frame();
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
