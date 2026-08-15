using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>randomsequence, ported from effects/effect_random_sequence.py. Transcribed from <c>effects/random_sequence.rs</c>.</summary>
public sealed class RandomSequenceConfig
{
    public double Speed { get; set; } = 0.007;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 8;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class RandomSequence : IEffect
{
    private const string DynamicNeutralGray = "808080";

    private readonly RandomSequenceConfig _config;
    private readonly List<CharId> _pendingChars;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private long _charactersPerTick;

    public RandomSequence(RandomSequenceConfig config)
    {
        _config = config;
        _pendingChars = new List<CharId>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _charactersPerTick = 1;
    }

    public static RandomSequence FromOptions(Dictionary<string, object> options)
    {
        return new RandomSequence(new RandomSequenceConfig
        {
            Speed = (double)options["--speed"],
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
        // random_sequence.rs:72 — (speed * input_len as f64) as i64 then max(..., 1)
        _charactersPerTick = System.Math.Max(
            PyCompat.TruncToI64(_config.Speed * world.Terminal.InputCharacters.Count),
            1);

        Color terminalBackgroundColor = world.Terminal.Config.TerminalBackgroundColor;
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
            world.Terminal.SetCharacterVisibility(id, false);

            string sceneId;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                sceneId = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            string[] symbols = [inputSymbol];
            long frames = _config.FinalGradientFrames;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(sceneId)
                    ?? throw new EngineInvariantException("random sequence scene");
                if (dynamic)
                {
                    Color? finalFg = finalColors.FgColor;
                    Color? finalBg = finalColors.BgColor;
                    if (finalFg is not null || finalBg is not null)
                    {
                        Gradient? fgGradient = finalFg is Color fg
                            ? Gradient.WithSteps([terminalBackgroundColor, fg], 7, false)
                            : null;
                        Gradient? bgGradient = finalBg is Color bg
                            ? Gradient.WithSteps([terminalBackgroundColor, bg], 7, false)
                            : null;
                        scene.ApplyGradientToSymbols(symbols, frames, fgGradient, bgGradient);
                    }
                    else
                    {
                        Gradient neutral = Gradient.WithSteps(
                            [terminalBackgroundColor, Color.FromHex(DynamicNeutralGray)],
                            7,
                            false);
                        scene.ApplyGradientToSymbols(symbols, frames, neutral, null);
                        scene.AddFrame(
                            inputSymbol,
                            frames,
                            new VisualParams { Colors = ColorPair.New(null, null) });
                    }
                }
                else
                {
                    Color finalFg = finalColors.FgColor
                        ?? throw new EngineInvariantException("gradient mapping fg");
                    Gradient gradient = Gradient.WithSteps([terminalBackgroundColor, finalFg], 7, false);
                    scene.ApplyGradientToSymbols(symbols, frames, gradient, null);
                }
            }

            world.ActivateScene(this, id, sceneId);
            _pendingChars.Add(id);
        }

        world.Rng.Shuffle(_pendingChars);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingChars.Count > 0 || !world.ActiveCharacters.IsEmpty)
        {
            for (long i = 0; i < _charactersPerTick; i++)
            {
                if (_pendingChars.Count > 0)
                {
                    CharId nextChar = _pendingChars[^1];
                    _pendingChars.RemoveAt(_pendingChars.Count - 1);
                    world.Terminal.SetCharacterVisibility(nextChar, true);
                    world.ActiveCharacters.Insert(
                        nextChar,
                        world.Terminal.Arena[(int)nextChar.Value].CharacterId);
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
