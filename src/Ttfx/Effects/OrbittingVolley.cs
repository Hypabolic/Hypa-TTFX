using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>
/// orbittingvolley, ported from effects/effect_orbittingvolley.py.
/// Transcribed from <c>effects/orbittingvolley.rs</c>.
/// </summary>
public sealed class OrbittingVolleyConfig
{
    public string TopLauncherSymbol { get; set; } = "█";
    public string RightLauncherSymbol { get; set; } = "█";
    public string BottomLauncherSymbol { get; set; } = "█";
    public string LeftLauncherSymbol { get; set; } = "█";
    public double LauncherMovementSpeed { get; set; } = 0.8;
    public double CharacterMovementSpeed { get; set; } = 1.5;
    public double VolleySize { get; set; } = 0.03;
    public long LaunchDelay { get; set; } = 30;
    public Easing CharacterEasing { get; set; } = Easing.OutSine;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Radial;
}

/// <summary>OrbittingVolleyIterator.Launcher.</summary>
internal sealed class Launcher
{
    public CharId Character { get; }
    public List<CharId> Magazine { get; }

    public Launcher(CharId character, List<CharId> magazine)
    {
        Character = character;
        Magazine = magazine;
    }
}

public sealed class OrbittingVolley : IEffect
{
    private readonly OrbittingVolleyConfig _config;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private CoordColorMap _launcherGradientCoordinateMap;
    private Color? _finalGradientLastColor;
    private readonly List<Launcher> _launchers;
    private long _delay;
    private bool _complete;

    public OrbittingVolley(OrbittingVolleyConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _launcherGradientCoordinateMap = new CoordColorMap();
        _finalGradientLastColor = null;
        _launchers = new List<Launcher>();
        _delay = 0;
        _complete = false;
    }

    public static OrbittingVolley FromOptions(Dictionary<string, object> options)
    {
        return new OrbittingVolley(new OrbittingVolleyConfig
        {
            TopLauncherSymbol = (string)options["--top-launcher-symbol"],
            RightLauncherSymbol = (string)options["--right-launcher-symbol"],
            BottomLauncherSymbol = (string)options["--bottom-launcher-symbol"],
            LeftLauncherSymbol = (string)options["--left-launcher-symbol"],
            LauncherMovementSpeed = (double)options["--launcher-movement-speed"],
            CharacterMovementSpeed = (double)options["--character-movement-speed"],
            VolleySize = (double)options["--volley-size"],
            LaunchDelay = (long)options["--launch-delay"],
            CharacterEasing = (Easing)options["--character-easing"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>Launcher.build_paths (only called for the main launcher).</summary>
    private void BuildLauncherPaths(EngineWorld world, CharId id)
    {
        Coord[] waypoints =
        [
            Coord.New(world.Terminal.Canvas.Left, world.Terminal.Canvas.Top),
            Coord.New(world.Terminal.Canvas.Right, world.Terminal.Canvas.Top),
        ];
        Coord inputCoord = world.Terminal.Arena[(int)id.Value].InputCoord;
        int waypointStartIndex = Array.FindIndex(waypoints, c => c.Equals(inputCoord));
        if (waypointStartIndex < 0)
        {
            throw new EngineInvariantException("launcher input coord not on perimeter waypoint list");
        }

        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        string perimeterPath = ch.Motion.NewPath(
            _config.LauncherMovementSpeed,
            null,
            2,
            0,
            false,
            "perimeter");
        Path path = ch.Motion.Paths.Get(perimeterPath)
            ?? throw new EngineInvariantException("perimeter path");
        for (int i = waypointStartIndex; i < waypoints.Length; i++)
        {
            path.NewWaypoint(waypoints[i], null, "");
        }

        for (int i = 0; i < waypointStartIndex; i++)
        {
            path.NewWaypoint(waypoints[i], null, "");
        }
    }

    /// <summary>Launcher.launch.</summary>
    private CharId? Launch(EngineWorld world, int launcherIndex)
    {
        Launcher launcher = _launchers[launcherIndex];
        if (launcher.Magazine.Count == 0)
        {
            return null;
        }

        // orbittingvolley.rs:138 — magazine.remove(0)
        CharId nextChar = launcher.Magazine[0];
        launcher.Magazine.RemoveAt(0);
        Coord launcherCoord = world.Terminal.Arena[(int)launcher.Character.Value].Motion.CurrentCoord;
        world.Terminal.Arena[(int)nextChar.Value].Motion.SetCoordinate(launcherCoord);
        world.ActivatePath(this, nextChar, "input_path");
        world.Terminal.SetCharacterVisibility(nextChar, true);
        return nextChar;
    }

    /// <summary>OrbittingVolleyIterator._set_launcher_coordinates.</summary>
    private void SetLauncherCoordinates(EngineWorld world, int parentIndex, int childIndex)
    {
        long canvasTop = world.Terminal.Canvas.Top;
        long canvasBottom = world.Terminal.Canvas.Bottom;
        long canvasLeft = world.Terminal.Canvas.Left;
        long canvasRight = world.Terminal.Canvas.Right;
        CharId parentChar = _launchers[parentIndex].Character;
        CharId childChar = _launchers[childIndex].Character;
        double parentProgress = world.Terminal.Arena[(int)parentChar.Value].Motion.CurrentCoord.Column
            / (double)canvasRight;
        Coord childInputCoord = world.Terminal.Arena[(int)childChar.Value].InputCoord;
        if (childInputCoord.Equals(Coord.New(canvasRight, canvasTop)))
        {
            // orbittingvolley.rs:158
            long childRow = canvasTop - PyCompat.TruncToI64(canvasTop * parentProgress);
            world.Terminal.Arena[(int)childChar.Value].Motion.SetCoordinate(
                Coord.New(canvasRight, Math.Max(1, childRow)));
        }
        else if (childInputCoord.Equals(Coord.New(canvasRight, canvasBottom)))
        {
            // orbittingvolley.rs:163
            long childColumn = canvasRight - PyCompat.TruncToI64(canvasRight * parentProgress);
            world.Terminal.Arena[(int)childChar.Value].Motion.SetCoordinate(
                Coord.New(Math.Max(1, childColumn), canvasBottom));
        }
        else if (childInputCoord.Equals(Coord.New(canvasLeft, canvasBottom)))
        {
            // orbittingvolley.rs:168
            long childRow = canvasBottom + PyCompat.TruncToI64(canvasTop * parentProgress);
            world.Terminal.Arena[(int)childChar.Value].Motion.SetCoordinate(
                Coord.New(canvasLeft, Math.Min(canvasTop, childRow)));
        }

        Coord currentCoord = world.Terminal.Arena[(int)childChar.Value].Motion.CurrentCoord;
        Color color = _launcherGradientCoordinateMap.Get(currentCoord)
            ?? throw new EngineInvariantException("launcher coord outside gradient map");
        EffectCharacter ch = world.Terminal.Arena[(int)childChar.Value];
        string inputSymbol = ch.InputSymbol;
        bool usesPre = ch.UsesInputPreexistingColors;
        ch.Animation.SetAppearance(
            inputSymbol,
            usesPre,
            inputSymbol,
            ColorPair.New(color, null));
    }

    public void Build(EngineWorld world)
    {
        Gradient finalGradient = Gradient.New(
            _config.FinalGradientStops,
            _config.FinalGradientSteps,
            false,
            false);
        CoordColorMap finalGradientCoordinateMap = finalGradient.BuildCoordinateColorMapping(
            world.Terminal.Canvas.TextBottom,
            world.Terminal.Canvas.TextTop,
            world.Terminal.Canvas.TextLeft,
            world.Terminal.Canvas.TextRight,
            _config.FinalGradientDirection);
        _launcherGradientCoordinateMap = finalGradient.BuildCoordinateColorMapping(
            world.Terminal.Canvas.Bottom,
            world.Terminal.Canvas.Top,
            world.Terminal.Canvas.Left,
            world.Terminal.Canvas.Right,
            _config.FinalGradientDirection);
        _finalGradientLastColor = finalGradient.Spectrum[^1];

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

            ColorPair finalColors;
            if (dynamic)
            {
                finalColors = ColorPair.New(inputFg, inputBg);
            }
            else
            {
                finalColors = ColorPair.New(
                    finalGradientCoordinateMap.Get(inputCoord)
                        ?? throw new EngineInvariantException("gradient mapping missing"),
                    null);
            }

            _characterFinalColorMap[id] = finalColors;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string inputPath = ch.Motion.NewPath(
                    _config.CharacterMovementSpeed,
                    _config.CharacterEasing,
                    1,
                    0,
                    false,
                    "input_path");
                ch.Motion.Paths.Get(inputPath)!
                    .NewWaypoint(inputCoord, null, "");
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path("input_path"),
                new EventAction.SetLayer(0));
            world.Terminal.Arena[(int)id.Value].Animation.SetAppearance(
                inputSymbol,
                usesPre,
                inputSymbol,
                finalColors);
        }

        (Coord coord, string symbol)[] launcherSpecs =
        [
            (Coord.New(world.Terminal.Canvas.Left, world.Terminal.Canvas.Top), _config.TopLauncherSymbol),
            (Coord.New(world.Terminal.Canvas.Right, world.Terminal.Canvas.Top), _config.RightLauncherSymbol),
            (Coord.New(world.Terminal.Canvas.Right, world.Terminal.Canvas.Bottom), _config.BottomLauncherSymbol),
            (Coord.New(world.Terminal.Canvas.Left, world.Terminal.Canvas.Bottom), _config.LeftLauncherSymbol),
        ];

        foreach ((Coord coord, string symbol) spec in launcherSpecs)
        {
            CharId character = world.Terminal.AddCharacter(spec.symbol, spec.coord);
            world.Terminal.Arena[(int)character.Value].Layer = 2;
            world.Terminal.SetCharacterVisibility(character, true);
            world.ActiveCharacters.Insert(
                character,
                world.Terminal.Arena[(int)character.Value].CharacterId);
            _launchers.Add(new Launcher(character, new List<CharId>()));
        }

        CharId mainCharacter = _launchers[0].Character;
        {
            Color? color = _finalGradientLastColor;
            EffectCharacter ch = world.Terminal.Arena[(int)mainCharacter.Value];
            string inputSymbol = ch.InputSymbol;
            bool usesPre = ch.UsesInputPreexistingColors;
            ch.Animation.SetAppearance(
                inputSymbol,
                usesPre,
                inputSymbol,
                ColorPair.New(color, null));
        }

        BuildLauncherPaths(world, mainCharacter);
        world.ActivatePath(this, mainCharacter, "perimeter");

        var sortedChars = new List<CharId>();
        foreach (List<CharId> charList in world.Terminal.GetCharactersGrouped(
                     CharacterFilter.Default,
                     CharacterGroup.CenterToOutside))
        {
            sortedChars.AddRange(charList);
        }

        for (int index = 0; index < sortedChars.Count; index++)
        {
            CharId character = sortedChars[index];
            int launcherIndex = index % _launchers.Count;
            _launchers[launcherIndex].Magazine.Add(character);
        }

        _delay = 0;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_launchers.Exists(l => l.Magazine.Count > 0) || world.ActiveCharacters.Count > 1)
        {
            CharId mainCharacter = _launchers[0].Character;
            if (world.Terminal.Arena[(int)mainCharacter.Value].Motion.ActivePath is null)
            {
                Coord firstWaypointCoord = world.Terminal.Arena[(int)mainCharacter.Value].Motion.Paths
                    .Get("perimeter")!
                    .Waypoints[0]
                    .Coord;
                world.Terminal.Arena[(int)mainCharacter.Value].Motion.SetCoordinate(firstWaypointCoord);
                world.ActivatePath(this, mainCharacter, "perimeter");
                world.ActiveCharacters.Insert(
                    mainCharacter,
                    world.Terminal.Arena[(int)mainCharacter.Value].CharacterId);
            }

            {
                Coord currentCoord = world.Terminal.Arena[(int)mainCharacter.Value].Motion.CurrentCoord;
                Color color = _launcherGradientCoordinateMap.Get(currentCoord)
                    ?? throw new EngineInvariantException("main launcher coord outside gradient map");
                string symbol = _config.TopLauncherSymbol;
                EffectCharacter ch = world.Terminal.Arena[(int)mainCharacter.Value];
                string inputSymbol = ch.InputSymbol;
                bool usesPre = ch.UsesInputPreexistingColors;
                ch.Animation.SetAppearance(
                    inputSymbol,
                    usesPre,
                    symbol,
                    ColorPair.New(color, null));
            }

            for (int childIndex = 1; childIndex < _launchers.Count; childIndex++)
            {
                SetLauncherCoordinates(world, 0, childIndex);
            }

            if (_delay == 0)
            {
                for (int launcherIndex = 0; launcherIndex < _launchers.Count; launcherIndex++)
                {
                    // orbittingvolley.rs:368 — max(int((volley_size * len(input_characters)) / 4), 1)
                    long charactersToLaunch = Math.Max(
                        1,
                        PyCompat.TruncToI64(
                            _config.VolleySize * world.Terminal.InputCharacters.Count / 4.0));
                    for (long i = 0; i < charactersToLaunch; i++)
                    {
                        CharId? nextChar = Launch(world, launcherIndex);
                        if (nextChar is CharId launched)
                        {
                            world.ActiveCharacters.Insert(
                                launched,
                                world.Terminal.Arena[(int)launched.Value].CharacterId);
                        }
                    }
                }

                _delay = _config.LaunchDelay;
            }
            else
            {
                _delay -= 1;
            }

            world.Update(this);
            return world.Frame();
        }

        if (!_complete)
        {
            _complete = true;
            for (int launcherIndex = 0; launcherIndex < _launchers.Count; launcherIndex++)
            {
                CharId character = _launchers[launcherIndex].Character;
                world.Terminal.SetCharacterVisibility(character, false);
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
