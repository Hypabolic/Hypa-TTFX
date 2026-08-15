using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>colorshift, ported from effects/effect_colorshift.py. Transcribed from <c>effects/colorshift.rs</c>.</summary>
public sealed class ColorShiftConfig
{
    public List<Color> GradientStops { get; set; } = new List<Color>();
    public List<long> GradientSteps { get; set; } = new List<long>();
    public long GradientFrames { get; set; } = 2;
    public bool NoTravel { get; set; }
    public GradientDirection TravelDirection { get; set; } = GradientDirection.Radial;
    public bool ReverseTravelDirection { get; set; }
    public bool NoLoop { get; set; }
    public long Cycles { get; set; } = 3;
    public bool SkipFinalGradient { get; set; }
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class ColorShift : IEffect
{
    private readonly ColorShiftConfig _config;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, Color> _characterFinalColorMap;
    private readonly Dictionary<CharId, long> _loopTrackerMap;

    public ColorShift(ColorShiftConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, Color>();
        _loopTrackerMap = new Dictionary<CharId, long>();
    }

    public static ColorShift FromOptions(Dictionary<string, object> options)
    {
        return new ColorShift(new ColorShiftConfig
        {
            GradientStops = TypedList<Color>(options, "--gradient-stops"),
            GradientSteps = TypedList<long>(options, "--gradient-steps"),
            GradientFrames = (long)options["--gradient-frames"],
            NoTravel = options.ContainsKey("--no-travel"),
            TravelDirection = (GradientDirection)options["--travel-direction"],
            ReverseTravelDirection = options.ContainsKey("--reverse-travel-direction"),
            NoLoop = options.ContainsKey("--no-loop"),
            Cycles = (long)options["--cycles"],
            SkipFinalGradient = options.ContainsKey("--skip-final-gradient"),
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    /// <summary>ColorShiftIterator.loop_tracker.</summary>
    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
        if (!_loopTrackerMap.TryGetValue(character, out long count))
        {
            count = 0;
        }

        count += 1;
        _loopTrackerMap[character] = count;
        // colorshift.rs:94 — cycles == 0 never terminates
        if (_config.Cycles == 0 || count < _config.Cycles)
        {
            world.ActivateScene(this, character, "gradient");
        }
        else if (!_config.SkipFinalGradient)
        {
            world.ActivateScene(this, character, "final_gradient");
        }
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
        List<CharId> charactersForMap = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in charactersForMap)
        {
            Coord inputCoord = world.Terminal.Arena[(int)id.Value].InputCoord;
            _characterFinalColorMap[id] = finalGradientMapping.Get(inputCoord)
                ?? throw new EngineInvariantException("gradient mapping missing");
        }

        Gradient gradient = Gradient.New(
            _config.GradientStops,
            _config.GradientSteps,
            false,
            !_config.NoLoop);
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            world.Terminal.SetCharacterVisibility(id, true);
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

            List<Color> colors;
            if (_config.NoTravel)
            {
                colors = new List<Color>(gradient.Spectrum);
            }
            else
            {
                double directionIndex = _config.TravelDirection switch
                {
                    GradientDirection.Horizontal => inputCoord.Column / (double)world.Terminal.Canvas.Right,
                    GradientDirection.Vertical => inputCoord.Row / (double)world.Terminal.Canvas.Top,
                    GradientDirection.Diagonal => (inputCoord.Row + inputCoord.Column)
                        / (double)(world.Terminal.Canvas.Right + world.Terminal.Canvas.Top),
                    GradientDirection.Radial => Geometry.FindNormalizedDistanceFromCenter(
                        world.Terminal.Canvas.TextBottom,
                        world.Terminal.Canvas.TextTop,
                        world.Terminal.Canvas.TextLeft,
                        world.Terminal.Canvas.TextRight,
                        inputCoord),
                    _ => throw new EngineInvariantException("travel direction"),
                };
                // colorshift.rs:167 — int() truncation
                long shiftDistance = PyCompat.TruncToI64(gradient.Spectrum.Count * directionIndex);
                if (_config.ReverseTravelDirection)
                {
                    shiftDistance *= -1;
                }

                long len = gradient.Spectrum.Count;
                int k = shiftDistance < 0
                    ? (int)Math.Max(len + shiftDistance, 0)
                    : (int)Math.Min(shiftDistance, len);
                var rotated = new List<Color>();
                rotated.AddRange(gradient.Spectrum.GetRange(k, gradient.Spectrum.Count - k));
                rotated.AddRange(gradient.Spectrum.GetRange(0, k));
                colors = rotated;
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.NewScene(false, null, null, "gradient", usesPre);
                Scene scene = ch.Animation.Scenes.Get("gradient")
                    ?? throw new EngineInvariantException("gradient scene");
                foreach (Color color in colors)
                {
                    scene.AddFrame(
                        inputSymbol,
                        _config.GradientFrames,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }

                ch.Animation.NewScene(false, null, null, "final_gradient", usesPre);
            }

            Color lastColor = colors[colors.Count - 1];
            if (dynamic)
            {
                Gradient? fgGradient = inputFg is not null
                    ? Gradient.WithSteps([lastColor, inputFg], 8, false)
                    : null;
                Gradient? bgGradient = inputBg is not null
                    ? Gradient.WithSteps([lastColor, inputBg], 8, false)
                    : null;
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get("final_gradient")
                    ?? throw new EngineInvariantException("final gradient scene");
                if (fgGradient is not null || bgGradient is not null)
                {
                    scene.ApplyGradientToSymbols(
                        [inputSymbol],
                        _config.GradientFrames,
                        fgGradient,
                        bgGradient);
                }
                else
                {
                    scene.AddFrame(
                        inputSymbol,
                        _config.GradientFrames,
                        new VisualParams { Colors = ColorPair.New(null, null) });
                }
            }
            else
            {
                Color finalColor = _characterFinalColorMap[id];
                Gradient finalSceneGradient = Gradient.WithSteps([lastColor, finalColor], 8, false);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get("final_gradient")
                    ?? throw new EngineInvariantException("final gradient scene");
                foreach (Color color in finalSceneGradient.Spectrum)
                {
                    scene.AddFrame(
                        inputSymbol,
                        _config.GradientFrames,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }
            }

            world.ActivateScene(this, id, "gradient");
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene("gradient"),
                new EventAction.Callback(new EffectCallback(0, [])));
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
