using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>waves, ported from effects/effect_waves.py. Transcribed from <c>effects/waves.rs</c>.</summary>
public sealed class WavesConfig
{
    public List<string> WaveSymbols { get; set; } = new List<string>();
    public List<Color> WaveGradientStops { get; set; } = new List<Color>();
    public List<long> WaveGradientSteps { get; set; } = new List<long>();
    public long WaveCount { get; set; } = 7;
    public long WaveLength { get; set; } = 2;
    public CharacterGroup WaveDirection { get; set; } = CharacterGroup.ColumnLeftToRight;
    public Easing WaveEasing { get; set; } = Easing.InOutSine;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
}

public sealed class Waves : IEffect
{
    private readonly WavesConfig _config;
    private readonly List<List<CharId>> _pendingColumns;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;

    public Waves(WavesConfig config)
    {
        _config = config;
        _pendingColumns = new List<List<CharId>>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    }

    public static Waves FromOptions(Dictionary<string, object> options)
    {
        return new Waves(new WavesConfig
        {
            WaveSymbols = TypedList<string>(options, "--wave-symbols"),
            WaveGradientStops = TypedList<Color>(options, "--wave-gradient-stops"),
            WaveGradientSteps = TypedList<long>(options, "--wave-gradient-steps"),
            WaveCount = (long)options["--wave-count"],
            WaveLength = (long)options["--wave-length"],
            WaveDirection = (CharacterGroup)options["--wave-direction"],
            WaveEasing = (Easing)options["--wave-easing"],
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
        Gradient waveGradient = Gradient.New(
            _config.WaveGradientStops,
            _config.WaveGradientSteps,
            false,
            false);

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

            ColorPair finalColors = dynamic
                ? ColorPair.New(inputFg, inputBg)
                : ColorPair.New(
                    finalGradientMapping.Get(inputCoord)
                        ?? throw new EngineInvariantException("gradient mapping missing"),
                    null);
            _characterFinalColorMap[id] = finalColors;

            string waveScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                waveScn = ch.Animation.NewScene(false, null, _config.WaveEasing, "", usesPre);
                Scene scene = ch.Animation.Scenes.Get(waveScn)
                    ?? throw new EngineInvariantException("wave scene");
                for (long i = 0; i < _config.WaveCount; i++)
                {
                    scene.ApplyGradientToSymbols(
                        _config.WaveSymbols,
                        _config.WaveLength,
                        waveGradient,
                        null);
                }
            }

            string finalScn = world.Terminal.Arena[(int)id.Value].Animation.NewScene(
                false,
                null,
                null,
                "",
                usesPre);

            if (dynamic)
            {
                Color? finalFgColor = finalColors.FgColor;
                Color? finalBgColor = finalColors.BgColor;
                if (finalFgColor is null && finalBgColor is null)
                {
                    world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(finalScn)!
                        .AddFrame(inputSymbol, 10, new VisualParams { Colors = new ColorPair() });
                }
                else
                {
                    Color waveLast = waveGradient.Spectrum[^1];
                    Gradient? fgGradient = finalFgColor is not null
                        ? Gradient.New([waveLast, finalFgColor], _config.FinalGradientSteps, false, false)
                        : null;
                    Gradient? bgGradient = finalBgColor is not null
                        ? Gradient.New([waveLast, finalBgColor], _config.FinalGradientSteps, false, false)
                        : null;
                    Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(finalScn)
                        ?? throw new EngineInvariantException("final scene");
                    scene.ApplyGradientToSymbols([inputSymbol], 10, fgGradient, bgGradient);
                    if (finalFgColor is null)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            10,
                            new VisualParams { Colors = ColorPair.New(null, finalBgColor) });
                    }
                }
            }
            else
            {
                Color finalFgColor = finalColors.FgColor
                    ?? throw new EngineInvariantException("gradient mapping fg");
                Gradient finalSceneGradient = Gradient.New(
                    [waveGradient.Spectrum[^1], finalFgColor],
                    _config.FinalGradientSteps,
                    false,
                    false);
                Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(finalScn)
                    ?? throw new EngineInvariantException("final scene");
                foreach (Color step in finalSceneGradient.Spectrum)
                {
                    scene.AddFrame(
                        inputSymbol,
                        10,
                        new VisualParams { Colors = ColorPair.New(step, null) });
                }
            }

            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene(waveScn),
                new EventAction.ActivateScene(finalScn));
            world.ActivateScene(this, id, waveScn);
            if (dynamic)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.SetAppearance(inputSymbol, usesPre, inputSymbol, finalColors);
            }
        }

        foreach (List<CharId> column in world.Terminal.GetCharactersGrouped(
                     CharacterFilter.Default,
                     _config.WaveDirection))
        {
            _pendingColumns.Add(column);
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingColumns.Count > 0 || !world.ActiveCharacters.IsEmpty)
        {
            if (_pendingColumns.Count > 0)
            {
                // waves.rs:271 — pending_columns.remove(0)
                List<CharId> nextColumn = _pendingColumns[0];
                _pendingColumns.RemoveAt(0);
                foreach (CharId id in nextColumn)
                {
                    world.Terminal.SetCharacterVisibility(id, true);
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
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
