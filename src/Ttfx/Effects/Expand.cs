using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>expand, ported from effects/effect_expand.py. Transcribed from <c>effects/expand.rs</c>.</summary>
public sealed class ExpandConfig
{
    public Easing ExpandEasing { get; set; } = Easing.InOutQuart;
    public double MovementSpeed { get; set; } = 0.35;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Expand : IEffect
{
    private readonly ExpandConfig _config;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;

    public Expand(ExpandConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    }

    public static Expand FromOptions(Dictionary<string, object> options)
    {
        return new Expand(new ExpandConfig
        {
            ExpandEasing = (Easing)options["--expand-easing"],
            MovementSpeed = (double)options["--movement-speed"],
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

            Coord center = world.Terminal.Canvas.Center;
            string inputCoordPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(center);
                string pathId = ch.Motion.NewPath(
                    _config.MovementSpeed,
                    _config.ExpandEasing,
                    null,
                    0,
                    false,
                    "");
                Path path = ch.Motion.Paths.Get(pathId)
                    ?? throw new EngineInvariantException("input coord path");
                path.NewWaypoint(inputCoord, null, "");
                inputCoordPath = pathId;
            }

            world.Terminal.SetCharacterVisibility(id, true);
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
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

            string gradientScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                gradientScn = ch.Animation.NewScene(false, SyncMetric.Distance, null, "", usesPre);
            }

            string[] symbols = [inputSymbol];
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(gradientScn)
                    ?? throw new EngineInvariantException("gradient scene");
                if (dynamic)
                {
                    Gradient? fgGradient = inputFg is Color fg
                        ? Gradient.WithSteps([finalGradient.Spectrum[0], fg], 10, false)
                        : null;
                    Gradient? bgGradient = inputBg is Color bg
                        ? Gradient.WithSteps([finalGradient.Spectrum[0], bg], 10, false)
                        : null;
                    if (fgGradient is not null || bgGradient is not null)
                    {
                        scene.ApplyGradientToSymbols(symbols, 1, fgGradient, bgGradient);
                    }
                    else
                    {
                        scene.AddFrame(
                            inputSymbol,
                            1,
                            new VisualParams { Colors = ColorPair.New(null, null) });
                    }
                }
                else
                {
                    if (!_characterFinalColorMap.TryGetValue(id, out ColorPair? pair))
                    {
                        throw new EngineInvariantException("gradient mapping missing");
                    }

                    Color finalFg = pair.FgColor
                        ?? throw new EngineInvariantException("gradient mapping fg");
                    Gradient gradient = Gradient.WithSteps(
                        [finalGradient.Spectrum[0], finalFg],
                        10,
                        false);
                    scene.ApplyGradientToSymbols(symbols, 5, gradient, null);
                }
            }

            world.ActivateScene(this, id, gradientScn);
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!world.ActiveCharacters.IsEmpty)
        {
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
