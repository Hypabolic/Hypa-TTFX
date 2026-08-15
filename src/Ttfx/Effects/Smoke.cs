using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>smoke, ported from effects/effect_smoke.py. Transcribed from <c>effects/smoke.rs</c>.</summary>
public sealed class SmokeConfig
{
    public Color StartingColor { get; set; } = Color.FromHex("7A7A7A");
    public List<string> SmokeSymbols { get; set; } = new List<string>();
    public List<Color> SmokeGradientStops { get; set; } = new List<Color>();
    public bool UseWholeCanvas { get; set; }
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Smoke : IEffect
{
    private readonly SmokeConfig _config;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    /// <summary>
    /// Option so next_frame can move it out of self while stepping needs ctx
    /// and event dispatch needs &amp;mut self.
    /// </summary>
    private BreadthFirst? _fillAlg;

    public Smoke(SmokeConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _fillAlg = null;
    }

    public static Smoke FromOptions(Dictionary<string, object> options)
    {
        return new Smoke(new SmokeConfig
        {
            StartingColor = (Color)options["--starting-color"],
            SmokeSymbols = TypedList<string>(options, "--smoke-symbols"),
            SmokeGradientStops = TypedList<Color>(options, "--smoke-gradient-stops"),
            UseWholeCanvas = options.ContainsKey("--use-whole-canvas"),
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
        bool limitToTextBoundary = !_config.UseWholeCanvas;
        PrimsWeighted genAlg = PrimsWeighted.New(world, null, limitToTextBoundary);
        Coord fillStartCoord = world.Terminal.Canvas.RandomCoord(world.Rng, false, limitToTextBoundary);
        CharId? fillStartChar = world.Terminal.GetCharacterByInputCoord(fillStartCoord);
        BreadthFirst fillAlg = BreadthFirst.New(world, fillStartChar, limitToTextBoundary);

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
        Color blk = Color.FromHex("000000");
        var smokeGradientColors = new List<Color>(_config.SmokeGradientStops);
        for (int i = _config.FinalGradientStops.Count - 1; i >= 0; i--)
        {
            smokeGradientColors.Add(_config.FinalGradientStops[i]);
        }

        Gradient smokeGradient = Gradient.New(smokeGradientColors, [3, 4], false, false);
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        var filter = new CharacterFilter(true, true, true, false);
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            filter,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            world.Terminal.SetCharacterVisibility(id, true);
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

            ColorPair baseColors;
            if (dynamic)
            {
                _characterFinalColorMap[id] = ColorPair.New(inputFg, inputBg);
                baseColors = ColorPair.New(blk, null);
            }
            else
            {
                Color mapped = finalGradientMapping.Get(inputCoord) ?? blk;
                _characterFinalColorMap[id] = ColorPair.New(mapped, null);
                baseColors = ColorPair.New(_config.StartingColor, null);
            }

            string paintScn = world.Terminal.Arena[(int)id.Value].Animation
                .NewScene(false, null, null, "paint", usesPre);
            if (dynamic)
            {
                ColorPair colors = _characterFinalColorMap[id];
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(paintScn)!
                    .AddFrame(inputSymbol, 5, new VisualParams { Colors = colors });
            }
            else
            {
                Color finalFgColor = _characterFinalColorMap[id].FgColor
                    ?? throw new EngineInvariantException("final fg color");
                var paintStops = new List<Color>(_config.FinalGradientStops);
                paintStops.Add(finalFgColor);
                Gradient paintGradient = Gradient.WithSteps(paintStops, 5, false);
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(paintScn)!
                    .ApplyGradientToSymbols([inputSymbol], 5, paintGradient, null);
            }

            string smokeScn = world.Terminal.Arena[(int)id.Value].Animation
                .NewScene(false, null, null, "smoke", usesPre);
            if (dynamic)
            {
                ColorPair colors = _characterFinalColorMap[id];
                Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(smokeScn)
                    ?? throw new EngineInvariantException("smoke scene");
                foreach (string smokeSymbol in _config.SmokeSymbols)
                {
                    scene.AddFrame(smokeSymbol, 10, new VisualParams { Colors = colors });
                }
            }
            else
            {
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(smokeScn)!
                    .ApplyGradientToSymbols(_config.SmokeSymbols, 3, smokeGradient, null);
            }

            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene(smokeScn),
                new EventAction.ActivateScene(paintScn));
            world.Terminal.Arena[(int)id.Value].Animation.SetAppearance(
                inputSymbol,
                usesPre,
                inputSymbol,
                baseColors);
        }

        while (!genAlg.Complete)
        {
            genAlg.Step(world);
        }

        CharId startingChar = fillAlg.StartingChar;
        world.ActivateScene(this, startingChar, "smoke");
        world.ActiveCharacters.Insert(
            startingChar,
            world.Terminal.Arena[(int)startingChar.Value].CharacterId);
        _fillAlg = fillAlg;
    }

    public string? NextFrame(EngineWorld world)
    {
        BreadthFirst fillAlg = _fillAlg ?? throw new EngineInvariantException("fill alg");
        _fillAlg = null;
        string? result;
        if (!fillAlg.Complete || !world.ActiveCharacters.IsEmpty)
        {
            if (!fillAlg.Complete)
            {
                fillAlg.Step(world);
                foreach (CharId id in fillAlg.ExploredLastStep)
                {
                    world.ActivateScene(this, id, "smoke");
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                }
            }

            world.Update(this);
            result = world.Frame();
        }
        else
        {
            result = null;
        }

        _fillAlg = fillAlg;
        return result;
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
