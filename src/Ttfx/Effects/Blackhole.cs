using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>BlackholeIterator.Phase.</summary>
public enum BlackholePhase
{
    Forming,
    Consuming,
    Collapsing,
    Exploding,
    Complete,
}

/// <summary>blackhole, ported from effects/effect_blackhole.py. Transcribed from <c>effects/blackhole.rs</c>.</summary>
public sealed class BlackholeConfig
{
    public Color BlackholeColor { get; set; } = Color.FromHex("ffffff");
    public List<Color> StarColors { get; set; } = new List<Color>();
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
}

public sealed class Blackhole : IEffect
{
    private readonly BlackholeConfig _config;
    private readonly List<CharId> _blackholeChars;
    private readonly List<CharId> _awaitingConsumptionChars;
    private long _blackholeRadius;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, Color> _characterFinalColorMap;
    private long _formationDelay;
    private long _fDelay;
    private BlackholePhase _phase;
    private List<CharId> _awaitingBlackholeChars;

    public Blackhole(BlackholeConfig config)
    {
        _config = config;
        _blackholeChars = new List<CharId>();
        _awaitingConsumptionChars = new List<CharId>();
        _blackholeRadius = 0;
        _characterFinalColorMap = new Dictionary<CharId, Color>();
        _formationDelay = 0;
        _fDelay = 0;
        _phase = BlackholePhase.Forming;
        _awaitingBlackholeChars = new List<CharId>();
    }

    public static Blackhole FromOptions(Dictionary<string, object> options)
    {
        return new Blackhole(new BlackholeConfig
        {
            BlackholeColor = (Color)options["--blackhole-color"],
            StarColors = TypedList<Color>(options, "--star-colors"),
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>BlackholeIterator.prepare_blackhole.</summary>
    private void PrepareBlackhole(EngineWorld world)
    {
        string[] starSymbols = ["*", "'", "`", "¤", "•", "°", "·"];
        List<Color> starfieldColors = Gradient.WithSteps(
            [Color.FromHex("#4a4a4d"), Color.FromHex("#ffffff")],
            6,
            false).Spectrum;
        var gradientMap = new List<Gradient>();
        foreach (Color color in starfieldColors)
        {
            gradientMap.Add(
                Gradient.WithSteps([color, Color.FromHex("#000000")], 10, false));
        }

        var availableChars = new List<CharId>(world.Terminal.InputCharacters);
        while (_blackholeChars.Count < _blackholeRadius * 3 && availableChars.Count > 0)
        {
            // blackhole.rs:103-104 — randrange(0, len) then RNG-indexed remove
            int index = (int)world.Rng.Randrange(0, availableChars.Count);
            _blackholeChars.Add(availableChars[index]);
            availableChars.RemoveAt(index);
        }

        List<Coord> blackHoleRingPositions = Geometry.FindCoordsOnCircle(
            world.Terminal.Canvas.Center,
            _blackholeRadius,
            _blackholeChars.Count,
            true);
        for (int positionIndex = 0; positionIndex < _blackholeChars.Count; positionIndex++)
        {
            CharId id = _blackholeChars[positionIndex];
            Coord startingPos = blackHoleRingPositions[positionIndex];
            string blackholePath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                blackholePath = ch.Motion.NewPath(0.7, Easing.InOutSine, null, 0, false, "blackhole");
                ch.Motion.Paths.Get(blackholePath)!
                    .NewWaypoint(startingPos, null, "");
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                bool usesPre = ch.UsesInputPreexistingColors;
                string blackholeScn = ch.Animation.NewScene(false, null, null, "blackhole", usesPre);
                ch.Animation.Scenes.Get(blackholeScn)!
                    .AddFrame(
                        "*",
                        1,
                        new VisualParams { Colors = ColorPair.New(_config.BlackholeColor, null) });
            }

            world.RegisterEvent(
                id,
                Event.PathActivated,
                new CallerKey.Path(blackholePath),
                new EventAction.SetLayer(1));

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string rotationPath = ch.Motion.NewPath(0.45, null, null, 0, true, "blackhole_rotation");
                var rotated = new List<Coord>();
                for (int i = positionIndex; i < blackHoleRingPositions.Count; i++)
                {
                    rotated.Add(blackHoleRingPositions[i]);
                }

                for (int i = 0; i < positionIndex; i++)
                {
                    rotated.Add(blackHoleRingPositions[i]);
                }

                Path path = ch.Motion.Paths.Get(rotationPath)
                    ?? throw new EngineInvariantException("rotation path");
                foreach (Coord coord in rotated)
                {
                    string waypointId = path.Waypoints.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    path.NewWaypoint(coord, null, waypointId);
                }
            }
        }

        var blackholeSet = new HashSet<CharId>(_blackholeChars);
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        Coord canvasCenter = world.Terminal.Canvas.Center;
        foreach (CharId id in characters)
        {
            world.Terminal.SetCharacterVisibility(id, true);
            string starSymbol = world.Rng.Choice(starSymbols);
            int starColorIndex = world.Rng.ChoiceIndex(starfieldColors.Count);
            Color starColor = starfieldColors[starColorIndex];
            string startingScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                bool usesPre = ch.UsesInputPreexistingColors;
                startingScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                ch.Animation.Scenes.Get(startingScn)!
                    .AddFrame(
                        starSymbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(starColor, null) });
            }

            world.ActivateScene(this, id, startingScn);
            if (!blackholeSet.Contains(id))
            {
                Coord starfieldCoord = world.Terminal.Canvas.RandomCoord(world.Rng, false, false);
                double speed = world.Rng.Uniform(0.17, 0.30);
                string singularityPath;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    ch.Motion.SetCoordinate(starfieldCoord);
                    singularityPath = ch.Motion.NewPath(speed, Easing.InExpo, null, 0, false, "singularity");
                    ch.Motion.Paths.Get(singularityPath)!
                        .NewWaypoint(canvasCenter, null, "");
                }

                string consumedScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    bool usesPre = ch.UsesInputPreexistingColors;
                    consumedScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                    Scene scene = ch.Animation.Scenes.Get(consumedScn)
                        ?? throw new EngineInvariantException("consumed scene");
                    foreach (Color color in gradientMap[starColorIndex].Spectrum)
                    {
                        scene.AddFrame(
                            starSymbol,
                            1,
                            new VisualParams { Colors = ColorPair.New(color, null) });
                    }

                    scene.AddFrame(" ", 1, new VisualParams());
                    scene.Sync = SyncMetric.Distance;
                }

                world.RegisterEvent(
                    id,
                    Event.PathActivated,
                    new CallerKey.Path(singularityPath),
                    new EventAction.SetLayer(2));
                world.RegisterEvent(
                    id,
                    Event.PathActivated,
                    new CallerKey.Path(singularityPath),
                    new EventAction.ActivateScene(consumedScn));
                _awaitingConsumptionChars.Add(id);
            }
        }

        world.Rng.Shuffle(_awaitingConsumptionChars);
    }

    /// <summary>BlackholeIterator.rotate_blackhole.</summary>
    private void RotateBlackhole(EngineWorld world)
    {
        foreach (CharId id in new List<CharId>(_blackholeChars))
        {
            world.ActivatePath(this, id, "blackhole_rotation");
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }
    }

    /// <summary>BlackholeIterator.collapse_blackhole.</summary>
    private void CollapseBlackhole(EngineWorld world)
    {
        var blackHoleRingPositions = Geometry.FindCoordsOnCircle(
            world.Terminal.Canvas.Center,
            _blackholeRadius + 3,
            _blackholeChars.Count,
            true);
        string[] unstableSymbols = ["◦", "◎", "◉", "●", "◉", "◎", "◦"];
        bool pointCharMade = false;
        Coord canvasCenter = world.Terminal.Canvas.Center;
        foreach (CharId id in new List<CharId>(_blackholeChars))
        {
            // blackhole.rs:287 — FIFO remove(0)
            Coord nextPos = blackHoleRingPositions[0];
            blackHoleRingPositions.RemoveAt(0);
            string expandPath;
            string collapsePath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                expandPath = ch.Motion.NewPath(0.2, Easing.InExpo, null, 0, false, "");
                ch.Motion.Paths.Get(expandPath)!.NewWaypoint(nextPos, null, "");
                collapsePath = ch.Motion.NewPath(0.3, Easing.InExpo, null, 0, false, "");
                ch.Motion.Paths.Get(collapsePath)!.NewWaypoint(canvasCenter, null, "");
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(expandPath),
                new EventAction.ActivatePath(collapsePath));
            if (!pointCharMade)
            {
                string pointScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    bool usesPre = ch.UsesInputPreexistingColors;
                    pointScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                }

                for (long round = 0; round < 3; round++)
                {
                    foreach (string symbol in unstableSymbols)
                    {
                        Color color = world.Rng.Choice(_config.StarColors);
                        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                        ch.Animation.Scenes.Get(pointScn)!
                            .AddFrame(
                                symbol,
                                3,
                                new VisualParams { Colors = ColorPair.New(color, null) });
                    }
                }

                world.RegisterEvent(
                    id,
                    Event.PathComplete,
                    new CallerKey.Path(collapsePath),
                    new EventAction.ActivateScene(pointScn));
                world.RegisterEvent(
                    id,
                    Event.PathComplete,
                    new CallerKey.Path(collapsePath),
                    new EventAction.SetLayer(3));
                pointCharMade = true;
            }

            world.ActivatePath(this, id, expandPath);
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }
    }

    /// <summary>BlackholeIterator.explode_singularity.</summary>
    private void ExplodeSingularity(EngineWorld world)
    {
        Color[] starColors =
        [
            Color.FromHex("#ffcc0d"),
            Color.FromHex("#ff7326"),
            Color.FromHex("#ff194d"),
            Color.FromHex("#bf2669"),
            Color.FromHex("#702a8c"),
            Color.FromHex("#049dbf"),
        ];
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
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

            List<Coord> circleCoords = Geometry.FindCoordsOnCircle(inputCoord, 3, 5, true);
            Coord nearbyCoord = circleCoords[(int)world.Rng.Randrange(0, 5)];
            double nearbySpeed = world.Rng.Randint(3, 4) / 10.0;
            string nearbyPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                nearbyPath = ch.Motion.NewPath(nearbySpeed, Easing.OutExpo, null, 0, false, "");
                ch.Motion.Paths.Get(nearbyPath)!.NewWaypoint(nearbyCoord, null, "");
            }

            double inputSpeed = world.Rng.Randint(4, 6) / 100.0;
            string inputPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputPath = ch.Motion.NewPath(inputSpeed, Easing.InCubic, null, 0, false, "");
                ch.Motion.Paths.Get(inputPath)!.NewWaypoint(inputCoord, null, "");
            }

            Color explodeStarColor = world.Rng.Choice(starColors);
            string explodeScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                explodeScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                ch.Animation.Scenes.Get(explodeScn)!
                    .AddFrame(
                        inputSymbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(explodeStarColor, null) });
            }

            string coolingScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                coolingScn = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            if (dynamic && world.PreexistingColorsPresent)
            {
                if (inputFg is null && inputBg is null)
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    ch.Animation.Scenes.Get(coolingScn)!
                        .AddFrame(
                            inputSymbol,
                            1,
                            new VisualParams { Colors = ColorPair.New(null, null) });
                }
                else
                {
                    Gradient? coolingGradientFg = inputFg is not null
                        ? Gradient.WithSteps([explodeStarColor, inputFg], 10, false)
                        : null;
                    Gradient? coolingGradientBg = inputBg is not null
                        ? Gradient.WithSteps([explodeStarColor, inputBg], 10, false)
                        : null;
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    ch.Animation.Scenes.Get(coolingScn)!
                        .ApplyGradientToSymbols(
                            [inputSymbol],
                            20,
                            coolingGradientFg,
                            coolingGradientBg);
                }
            }
            else
            {
                Color finalColor = _characterFinalColorMap[id];
                Gradient coolingGradient = Gradient.WithSteps([explodeStarColor, finalColor], 10, false);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.Scenes.Get(coolingScn)!
                    .ApplyGradientToSymbols([inputSymbol], 20, coolingGradient, null);
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(nearbyPath),
                new EventAction.ActivatePath(inputPath));
            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(nearbyPath),
                new EventAction.ActivateScene(coolingScn));
            world.ActivateScene(this, id, explodeScn);
            world.ActivatePath(this, id, nearbyPath);
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }
    }

    public void Build(EngineWorld world)
    {
        // BlackholeIterator.__init__
        _blackholeRadius = Math.Max(
            Math.Min(
                PyCompat.RoundHalfEven(world.Terminal.Canvas.Width * 0.3),
                PyCompat.RoundHalfEven(world.Terminal.Canvas.Height * 0.20)),
            3);
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
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            Coord inputCoord = world.Terminal.Arena[(int)id.Value].InputCoord;
            _characterFinalColorMap[id] = finalGradientMapping.Get(inputCoord)
                ?? throw new EngineInvariantException("gradient mapping missing");
        }

        PrepareBlackhole(world);
        // blackhole.rs:556 — floor_div, not C# /
        _formationDelay = Math.Max(PyCompat.FloorDiv(100, _blackholeChars.Count), 6);
        _fDelay = _formationDelay;
        _phase = BlackholePhase.Forming;
        _awaitingBlackholeChars = new List<CharId>(_blackholeChars);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!world.ActiveCharacters.IsEmpty || _phase != BlackholePhase.Complete)
        {
            switch (_phase)
            {
                case BlackholePhase.Forming:
                    if (_awaitingBlackholeChars.Count > 0)
                    {
                        if (_fDelay == 0)
                        {
                            // blackhole.rs:569 — FIFO remove(0)
                            CharId nextChar = _awaitingBlackholeChars[0];
                            _awaitingBlackholeChars.RemoveAt(0);
                            world.ActivatePath(this, nextChar, "blackhole");
                            world.ActivateScene(this, nextChar, "blackhole");
                            world.ActiveCharacters.Insert(
                                nextChar,
                                world.Terminal.Arena[(int)nextChar.Value].CharacterId);
                            _fDelay = _formationDelay;
                        }
                        else
                        {
                            _fDelay -= 1;
                        }
                    }
                    else if (world.ActiveCharacters.IsEmpty)
                    {
                        RotateBlackhole(world);
                        _phase = BlackholePhase.Consuming;
                    }

                    break;
                case BlackholePhase.Consuming:
                    if (_awaitingConsumptionChars.Count > 0)
                    {
                        foreach (CharId id in new List<CharId>(_awaitingConsumptionChars))
                        {
                            world.ActivatePath(this, id, "singularity");
                            world.ActiveCharacters.Insert(
                                id,
                                world.Terminal.Arena[(int)id.Value].CharacterId);
                        }

                        _awaitingConsumptionChars.Clear();
                    }
                    else
                    {
                        var blackholeSet = new HashSet<CharId>(_blackholeChars);
                        bool allBlackhole = true;
                        foreach (CharId id in world.ActiveCharacters.Snapshot())
                        {
                            if (!blackholeSet.Contains(id))
                            {
                                allBlackhole = false;
                                break;
                            }
                        }

                        if (allBlackhole)
                        {
                            _phase = BlackholePhase.Collapsing;
                        }
                    }

                    break;
                case BlackholePhase.Collapsing:
                    CollapseBlackhole(world);
                    _phase = BlackholePhase.Exploding;
                    break;
                case BlackholePhase.Exploding:
                    bool allIdle = true;
                    foreach (CharId id in _blackholeChars)
                    {
                        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                        if (ch.Motion.ActivePath is not null || ch.Animation.ActiveScene is not null)
                        {
                            allIdle = false;
                            break;
                        }
                    }

                    if (allIdle)
                    {
                        ExplodeSingularity(world);
                        _phase = BlackholePhase.Complete;
                    }

                    break;
                case BlackholePhase.Complete:
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
