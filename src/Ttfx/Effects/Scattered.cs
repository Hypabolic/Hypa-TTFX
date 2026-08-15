using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>scattered, ported from effects/effect_scattered.py. Transcribed from <c>effects/scattered.rs</c>.</summary>
public sealed class ScatteredConfig
{
    public double MovementSpeed { get; set; } = 0.5;
    public Easing MovementEasing { get; set; } = Easing.InOutBack;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 9;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Scattered : IEffect
{
    private readonly ScatteredConfig _config;
    private readonly List<CharId> _pendingChars;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private long _initialHoldFrames;

    public Scattered(ScatteredConfig config)
    {
        _config = config;
        _pendingChars = new List<CharId>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _initialHoldFrames = 0;
    }

    public static Scattered FromOptions(Dictionary<string, object> options)
    {
        return new Scattered(new ScatteredConfig
        {
            MovementSpeed = (double)options["--movement-speed"],
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
            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
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
            Coord startCoord;
            if (world.Terminal.Canvas.Right < 2 || world.Terminal.Canvas.Top < 2)
            {
                startCoord = Coord.New(1, 1);
            }
            else
            {
                startCoord = world.Terminal.Canvas.RandomCoord(world.Rng, false, false);
            }

            string inputCoordPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(startCoord);
                string pathId = ch.Motion.NewPath(
                    _config.MovementSpeed,
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
            world.ActivatePath(this, id, inputCoordPath);
            world.Terminal.SetCharacterVisibility(id, true);

            string gradientScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                gradientScn = ch.Animation.NewScene(false, SyncMetric.Distance, null, "", usesPre);
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
                        [finalGradient.Spectrum[0], finalFgColor],
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
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }

        _initialHoldFrames = 25;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingChars.Count > 0 || !world.ActiveCharacters.IsEmpty)
        {
            if (_initialHoldFrames != 0)
            {
                _initialHoldFrames -= 1;
                return world.Frame();
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
