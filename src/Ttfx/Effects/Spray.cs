using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>SprayIterator.SprayPosition.</summary>
public enum SprayPosition
{
    N,
    Ne,
    E,
    Se,
    S,
    Sw,
    W,
    Nw,
    Center,
}

/// <summary>spray, ported from effects/effect_spray.py. Transcribed from <c>effects/spray.rs</c>.</summary>
public sealed class SprayConfig
{
    public SprayPosition SprayPosition { get; set; } = SprayPosition.E;
    public double SprayVolume { get; set; } = 0.005;
    public (double Min, double Max) MovementSpeedRange { get; set; } = (0.6, 1.4);
    public Easing MovementEasing { get; set; } = Easing.OutExpo;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Spray : IEffect
{
    private readonly SprayConfig _config;
    private readonly List<CharId> _pendingChars;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private long _volume;

    public Spray(SprayConfig config)
    {
        _config = config;
        _pendingChars = new List<CharId>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _volume = 1;
    }

    /// <summary>spray.rs parse_spray_position.</summary>
    public static object ParseSprayPosition(string s)
    {
        return s switch
        {
            "n" => SprayPosition.N,
            "ne" => SprayPosition.Ne,
            "e" => SprayPosition.E,
            "se" => SprayPosition.Se,
            "s" => SprayPosition.S,
            "sw" => SprayPosition.Sw,
            "w" => SprayPosition.W,
            "nw" => SprayPosition.Nw,
            "center" => SprayPosition.Center,
            _ => throw new UsageError(
                $"invalid choice: '{s}' (choose from 'n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw', 'center')"),
        };
    }

    public static Spray FromOptions(Dictionary<string, object> options)
    {
        (double min, double max) = ((double, double))options["--movement-speed-range"];
        return new Spray(new SprayConfig
        {
            SprayPosition = (SprayPosition)options["--spray-position"],
            SprayVolume = (double)options["--spray-volume"],
            MovementSpeedRange = (min, max),
            MovementEasing = (Easing)options["--movement-easing"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
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

        Canvas canvas = world.Terminal.Canvas;
        Coord sprayOrigin = _config.SprayPosition switch
        {
            SprayPosition.Center => canvas.Center,
            SprayPosition.N => Coord.New(PyCompat.FloorDiv(canvas.Right, 2), canvas.Top),
            SprayPosition.Nw => Coord.New(canvas.Left, canvas.Top),
            SprayPosition.W => Coord.New(canvas.Left, PyCompat.FloorDiv(canvas.Top, 2)),
            SprayPosition.Sw => Coord.New(canvas.Left, canvas.Bottom),
            SprayPosition.S => Coord.New(PyCompat.FloorDiv(canvas.Right, 2), canvas.Bottom),
            SprayPosition.Se => Coord.New(canvas.Right - 1, canvas.Bottom),
            SprayPosition.E => Coord.New(canvas.Right - 1, PyCompat.FloorDiv(canvas.Top, 2)),
            SprayPosition.Ne => Coord.New(canvas.Right - 1, canvas.Top),
            _ => throw new EngineInvariantException("spray position"),
        };

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

            double speed = world.Rng.Uniform(_config.MovementSpeedRange.Min, _config.MovementSpeedRange.Max);
            string inputCoordPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(sprayOrigin);
                string pathId = ch.Motion.NewPath(
                    speed,
                    _config.MovementEasing,
                    null,
                    0,
                    false,
                    "");
                Path path = ch.Motion.Paths.Get(pathId)
                    ?? throw new EngineInvariantException("input coord path");
                path.NewWaypoint(inputCoord, null, "");
                inputCoordPath = pathId;
            }

            world.RegisterEvent(
                id,
                Event.PathActivated,
                new CallerKey.Path(inputCoordPath),
                new EventAction.SetLayer(1));
            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(inputCoordPath),
                new EventAction.SetLayer(0));

            string dropletScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                dropletScn = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            ColorPair finalColors = _characterFinalColorMap[id];
            if (dynamic)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(dropletScn)
                    ?? throw new EngineInvariantException("droplet scene");
                for (int i = 0; i < 7; i++)
                {
                    scene.AddFrame(
                        inputSymbol,
                        20,
                        new VisualParams { Colors = finalColors });
                }
            }
            else
            {
                Color startColor = world.Rng.Choice(finalGradient.Spectrum);
                Color finalFg = finalColors.FgColor
                    ?? throw new EngineInvariantException("gradient mapping fg");
                Gradient sprayGradient = Gradient.WithSteps([startColor, finalFg], 7, false);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(dropletScn)
                    ?? throw new EngineInvariantException("droplet scene");
                scene.ApplyGradientToSymbols([inputSymbol], 20, sprayGradient, null);
            }

            world.ActivateScene(this, id, dropletScn);
            world.ActivatePath(this, id, inputCoordPath);
            _pendingChars.Add(id);
        }

        world.Rng.Shuffle(_pendingChars);
        // spray.rs:222 — (pending.len() as f64 * spray_volume) as i64 then max(..., 1)
        _volume = System.Math.Max(
            PyCompat.TruncToI64(_pendingChars.Count * _config.SprayVolume),
            1);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingChars.Count > 0 || !world.ActiveCharacters.IsEmpty)
        {
            if (_pendingChars.Count > 0)
            {
                long count = world.Rng.Randint(1, _volume);
                for (long i = 0; i < count; i++)
                {
                    if (_pendingChars.Count > 0)
                    {
                        CharId nextCharacter = _pendingChars[^1];
                        _pendingChars.RemoveAt(_pendingChars.Count - 1);
                        world.Terminal.SetCharacterVisibility(nextCharacter, true);
                        world.ActiveCharacters.Insert(
                            nextCharacter,
                            world.Terminal.Arena[(int)nextCharacter.Value].CharacterId);
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
