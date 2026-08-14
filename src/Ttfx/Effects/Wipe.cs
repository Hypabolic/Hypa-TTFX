using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>wipe, ported from effects/effect_wipe.py. Transcribed from <c>effects/wipe.rs</c>.</summary>
public sealed class WipeConfig
{
    public CharacterGroup WipeDirection { get; set; } = CharacterGroup.DiagonalTopLeftToBottomRight;
    public long WipeDelay { get; set; }
    public Easing WipeEase { get; set; } = Easing.InOutCirc;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 3;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Wipe : IEffect
{
    private readonly WipeConfig _config;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private SequenceEaser<List<CharId>>? _easer;
    private long _wipeDelay;

    public Wipe(WipeConfig config)
    {
        _wipeDelay = config.WipeDelay;
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _easer = null;
    }

    public static Wipe FromOptions(Dictionary<string, object> options)
    {
        return new Wipe(new WipeConfig
        {
            WipeDirection = (CharacterGroup)options["--wipe-direction"],
            WipeDelay = (long)options["--wipe-delay"],
            WipeEase = (Easing)options["--wipe-ease"],
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
        List<List<CharId>> groups = world.Terminal.GetCharactersGrouped(
            CharacterFilter.Default,
            _config.WipeDirection);
        _easer = new SequenceEaser<List<CharId>>(groups, _config.WipeEase, 100);

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

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.NewScene(false, null, null, "wipe", usesPre);
                Scene scene = ch.Animation.Scenes.Get("wipe")
                    ?? throw new EngineInvariantException("wipe scene");
                if (dynamic)
                {
                    long frameCount = 0;
                    foreach (long step in _config.FinalGradientSteps)
                    {
                        frameCount += step;
                    }

                    frameCount += 1;
                    for (long i = 0; i < frameCount; i++)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            _config.FinalGradientFrames,
                            new VisualParams { Colors = finalColors });
                    }
                }
                else
                {
                    Color finalFg = finalColors.FgColor
                        ?? throw new EngineInvariantException("gradient mapping fg");
                    Gradient wipeGradient = Gradient.New(
                        [finalGradient.Spectrum[0], finalFg],
                        _config.FinalGradientSteps,
                        false,
                        false);
                    scene.ApplyGradientToSymbols(
                        [inputSymbol],
                        _config.FinalGradientFrames,
                        wipeGradient,
                        null);
                }
            }
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        bool easerComplete = _easer!.IsComplete();
        if (!world.ActiveCharacters.IsEmpty || !easerComplete)
        {
            if (_wipeDelay == 0)
            {
                SequenceEaser<List<CharId>> easer = _easer!;
                _easer = null;
                SequenceStep<List<CharId>> step = easer.Step();
                foreach (List<CharId> group in step.Added)
                {
                    foreach (CharId id in group)
                    {
                        world.ActivateScene(this, id, "wipe");
                        world.Terminal.SetCharacterVisibility(id, true);
                        world.ActiveCharacters.Insert(id, world.Terminal.Arena[(int)id.Value].CharacterId);
                    }
                }

                foreach (List<CharId> group in step.Removed)
                {
                    foreach (CharId id in group)
                    {
                        world.DeactivateScene(id, null);
                        world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("wipe")!.ResetScene();
                        world.Terminal.SetCharacterVisibility(id, false);
                    }
                }

                _easer = easer;
                _wipeDelay = _config.WipeDelay;
            }
            else
            {
                _wipeDelay -= 1;
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
