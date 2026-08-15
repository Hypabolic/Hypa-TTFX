using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>fireworks, ported from effects/effect_fireworks.py. Transcribed from <c>effects/fireworks.rs</c>.</summary>
public sealed class FireworksConfig
{
    public bool ExplodeAnywhere { get; set; }
    public List<Color> FireworkColors { get; set; } = new List<Color>();
    public string FireworkSymbol { get; set; } = "o";
    public double FireworkVolume { get; set; } = 0.05;
    public long LaunchDelay { get; set; } = 45;
    public double ExplodeDistance { get; set; } = 0.2;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Horizontal;
}

public sealed class Fireworks : IEffect
{
    private readonly FireworksConfig _config;
    private readonly List<List<CharId>> _shells;
    private long _fireworkVolume;
    private long _explodeDistance;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private long _launchDelay;

    public Fireworks(FireworksConfig config)
    {
        _config = config;
        _shells = new List<List<CharId>>();
        _fireworkVolume = 0;
        _explodeDistance = 0;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _launchDelay = 0;
    }

    public static Fireworks FromOptions(Dictionary<string, object> options)
    {
        return new Fireworks(new FireworksConfig
        {
            ExplodeAnywhere = options.ContainsKey("--explode-anywhere"),
            FireworkColors = TypedList<Color>(options, "--firework-colors"),
            FireworkSymbol = (string)options["--firework-symbol"],
            FireworkVolume = (double)options["--firework-volume"],
            LaunchDelay = (long)options["--launch-delay"],
            ExplodeDistance = (double)options["--explode-distance"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>FireworksIterator.prepare_waypoints.</summary>
    private void PrepareWaypoints(EngineWorld world)
    {
        var fireworkShell = new List<CharId>();
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        long canvasBottom = world.Terminal.Canvas.Bottom;
        long canvasTop = world.Terminal.Canvas.Top;
        long canvasRight = world.Terminal.Canvas.Right;
        long originX = 0;
        Coord originCoord = Coord.New(0, 0);
        List<Coord> explodeWaypointCoords = new List<Coord>();
        foreach (CharId id in characters)
        {
            if (fireworkShell.Count == _fireworkVolume || fireworkShell.Count == 0)
            {
                originX = world.Rng.Randrange(0, canvasRight);
                _shells.Add(fireworkShell);
                fireworkShell = new List<CharId>();
                long minRow = !_config.ExplodeAnywhere
                    ? world.Terminal.Arena[(int)id.Value].InputCoord.Row
                    : canvasBottom;
                long originY = world.Rng.Randrange(minRow, canvasTop + 1);
                originCoord = Coord.New(originX, originY);
                explodeWaypointCoords = Geometry.FindCoordsInCircle(originCoord, _explodeDistance);
            }

            Coord inputCoord = world.Terminal.Arena[(int)id.Value].InputCoord;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(Coord.New(originX, canvasBottom));
                string apexPath = ch.Motion.NewPath(0.35, Easing.OutExpo, 2, 0, false, "apex_pth");
                ch.Motion.Paths.Get(apexPath)!.NewWaypoint(originCoord, null, "");
            }

            Coord apexWptCoord = originCoord;
            double explodeSpeed = world.Rng.Uniform(0.2, 0.4);
            string explodePath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                explodePath = ch.Motion.NewPath(explodeSpeed, Easing.OutCirc, 2, 0, false, "");
            }

            Coord explodeWptCoord = world.Rng.Choice(explodeWaypointCoords);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.Paths.Get(explodePath)!.NewWaypoint(explodeWptCoord, null, "");
            }

            Coord bloomControlPoint = Geometry.ExtrapolateAlongRay(
                apexWptCoord,
                explodeWptCoord,
                (double)PyCompat.FloorDiv(_explodeDistance, 2));
            Coord bloomWptCoord = Coord.New(bloomControlPoint.Column, Math.Max(1, bloomControlPoint.Row - 7));
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.Paths.Get(explodePath)!
                    .NewWaypoint(bloomWptCoord, [bloomControlPoint], "");
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string inputPath = ch.Motion.NewPath(0.6, Easing.InOutQuart, 2, 0, false, "input_pth");
                Coord inputControlPoint = Coord.New(bloomWptCoord.Column, 1);
                ch.Motion.Paths.Get(inputPath)!
                    .NewWaypoint(inputCoord, [inputControlPoint], "");
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path("apex_pth"),
                new EventAction.ActivatePath(explodePath));
            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(explodePath),
                new EventAction.ActivatePath("input_pth"));
            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path("input_pth"),
                new EventAction.SetLayer(0));
            world.ActivatePath(this, id, "apex_pth");
            fireworkShell.Add(id);
        }

        if (fireworkShell.Count > 0)
        {
            _shells.Add(fireworkShell);
        }
    }

    /// <summary>FireworksIterator.prepare_scenes.</summary>
    private void PrepareScenes(EngineWorld world)
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
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
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

        Color white = Color.FromHex("FFFFFF");
        var shellsCopy = new List<List<CharId>>(_shells);
        foreach (List<CharId> fireworkShell in shellsCopy)
        {
            Color shellColor = world.Rng.Choice(_config.FireworkColors);
            Gradient shellGradient = Gradient.WithSteps([shellColor, white, shellColor], 5, false);
            foreach (CharId id in fireworkShell)
            {
                string inputSymbol;
                Color? inputFg;
                Color? inputBg;
                bool usesPre;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputSymbol = ch.InputSymbol;
                    inputFg = ch.Animation.InputFgColor;
                    inputBg = ch.Animation.InputBgColor;
                    usesPre = ch.UsesInputPreexistingColors;
                }

                string launchScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    launchScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                    Scene scene = ch.Animation.Scenes.Get(launchScn)
                        ?? throw new EngineInvariantException("launch scene");
                    scene.AddFrame(
                        _config.FireworkSymbol,
                        2,
                        new VisualParams { Colors = ColorPair.New(shellColor, null) });
                    scene.AddFrame(
                        _config.FireworkSymbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(white, null) });
                    scene.IsLooping = true;
                }

                string bloomScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    bloomScn = ch.Animation.NewScene(false, SyncMetric.Step, null, "", usesPre);
                    Scene scene = ch.Animation.Scenes.Get(bloomScn)
                        ?? throw new EngineInvariantException("bloom scene");
                    foreach (Color color in shellGradient.Spectrum)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            2,
                            new VisualParams { Colors = ColorPair.New(color, null) });
                    }
                }

                string fallScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    fallScn = ch.Animation.NewScene(false, null, null, "fall_scn", usesPre);
                }

                if (dynamic)
                {
                    Gradient? fgGradient = inputFg is not null
                        ? Gradient.WithSteps([shellColor, inputFg], 15, false)
                        : null;
                    Gradient? bgGradient = inputBg is not null
                        ? Gradient.WithSteps([shellColor, inputBg], 15, false)
                        : null;
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    Scene scene = ch.Animation.Scenes.Get(fallScn)
                        ?? throw new EngineInvariantException("fall scene");
                    if (fgGradient is not null || bgGradient is not null)
                    {
                        scene.ApplyGradientToSymbols([inputSymbol], 10, fgGradient, bgGradient);
                    }
                    else
                    {
                        scene.AddFrame(
                            inputSymbol,
                            10,
                            new VisualParams { Colors = ColorPair.New(null, null) });
                    }
                }
                else
                {
                    Color finalFg = _characterFinalColorMap[id].FgColor
                        ?? throw new EngineInvariantException("gradient mapping fg");
                    Gradient fallGradient = Gradient.WithSteps([shellColor, finalFg], 15, false);
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    ch.Animation.Scenes.Get(fallScn)!
                        .ApplyGradientToSymbols([inputSymbol], 10, fallGradient, null);
                }

                world.ActivateScene(this, id, launchScn);
                world.RegisterEvent(
                    id,
                    Event.PathComplete,
                    new CallerKey.Path("apex_pth"),
                    new EventAction.ActivateScene(bloomScn));
                world.RegisterEvent(
                    id,
                    Event.PathActivated,
                    new CallerKey.Path("input_pth"),
                    new EventAction.ActivateScene(fallScn));
            }
        }
    }

    public void Build(EngineWorld world)
    {
        // __init__ precomputations (no RNG)
        _fireworkVolume = Math.Max(
            1,
            PyCompat.RoundHalfEven(_config.FireworkVolume * world.Terminal.InputCharacters.Count));
        _explodeDistance = Math.Min(
            15,
            Math.Max(1, PyCompat.RoundHalfEven(world.Terminal.Canvas.Right * _config.ExplodeDistance)));
        _launchDelay = 0;
        PrepareWaypoints(world);
        PrepareScenes(world);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_shells.Count > 0 || !world.ActiveCharacters.IsEmpty)
        {
            if (_shells.Count > 0 && _launchDelay <= 0)
            {
                List<CharId> nextGroup = _shells[_shells.Count - 1];
                _shells.RemoveAt(_shells.Count - 1);
                foreach (CharId id in nextGroup)
                {
                    world.Terminal.SetCharacterVisibility(id, true);
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                }

                // fireworks.rs:413 — int(launch_delay * uniform(0.5, 1.5)) truncation
                _launchDelay = PyCompat.TruncToI64(_config.LaunchDelay * world.Rng.Uniform(0.5, 1.5));
            }

            _launchDelay -= 1;
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
