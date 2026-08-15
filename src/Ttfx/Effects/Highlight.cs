using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>highlight, ported from effects/effect_highlight.py. Transcribed from <c>effects/highlight.rs</c>.</summary>
public sealed class HighlightConfig
{
    public double HighlightBrightness { get; set; } = 1.75;
    public CharacterGroup HighlightDirection { get; set; } = CharacterGroup.DiagonalBottomLeftToTopRight;
    public long HighlightWidth { get; set; } = 8;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Highlight : IEffect
{
    private readonly HighlightConfig _config;
    // Upstream stores this map; nothing reads it (faithful).
    private readonly Dictionary<CharId, Color?> _characterFinalColorMap;
    private SequenceEaser<List<CharId>>? _easer;

    public Highlight(HighlightConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, Color?>();
        _easer = null;
    }

    public static Highlight FromOptions(Dictionary<string, object> options)
    {
        return new Highlight(new HighlightConfig
        {
            HighlightBrightness = (double)options["--highlight-brightness"],
            HighlightDirection = (CharacterGroup)options["--highlight-direction"],
            HighlightWidth = (long)options["--highlight-width"],
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
        List<List<CharId>> groups = world.Terminal.GetCharactersGrouped(
            CharacterFilter.Default,
            _config.HighlightDirection);
        _easer = new SequenceEaser<List<CharId>>(groups, Easing.InOutCirc, 100);

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

            Color? baseColor;
            Color? inputBgColor;
            if (dynamic)
            {
                baseColor = inputFg;
                inputBgColor = inputBg;
            }
            else
            {
                baseColor = finalGradientMapping.Get(inputCoord)
                    ?? throw new EngineInvariantException("gradient mapping missing");
                inputBgColor = null;
            }

            _characterFinalColorMap[id] = baseColor;
            ColorPair baseColors = ColorPair.New(baseColor, inputBgColor);

            Gradient? highlightGradient = null;
            if (baseColor is not null)
            {
                Color highlightColor = Animation.AdjustColorBrightness(baseColor, _config.HighlightBrightness);
                highlightGradient = Gradient.New(
                    [baseColor, highlightColor, highlightColor, baseColor],
                    [3, _config.HighlightWidth, 3],
                    false,
                    false);
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.SetAppearance(inputSymbol, usesPre, inputSymbol, baseColors);
                ch.Animation.NewScene(false, null, null, "highlight", usesPre);
                Scene scene = ch.Animation.Scenes.Get("highlight")
                    ?? throw new EngineInvariantException("highlight scene");
                if (highlightGradient is not null)
                {
                    foreach (Color color in highlightGradient.Spectrum)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            2,
                            new VisualParams
                            {
                                Colors = ColorPair.New(color, inputBgColor),
                            });
                    }
                }
                else
                {
                    scene.AddFrame(
                        inputSymbol,
                        2,
                        new VisualParams { Colors = baseColors });
                }
            }

            world.Terminal.SetCharacterVisibility(id, true);
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        bool easerComplete = _easer!.IsComplete();
        if (!world.ActiveCharacters.IsEmpty || !easerComplete)
        {
            SequenceEaser<List<CharId>> easer = _easer!;
            _easer = null;
            SequenceStep<List<CharId>> step = easer.Step();
            foreach (List<CharId> group in step.Added)
            {
                foreach (CharId id in group)
                {
                    world.ActivateScene(this, id, "highlight");
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                }
            }

            _easer = easer;
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
