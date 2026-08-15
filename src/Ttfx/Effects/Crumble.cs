using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>CrumbleIterator.Stage.</summary>
public enum CrumbleStage
{
    Falling,
    Vacuuming,
    Resetting,
    Complete,
}

/// <summary>crumble, ported from effects/effect_crumble.py. Transcribed from <c>effects/crumble.rs</c>.</summary>
public sealed class CrumbleConfig
{
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
}

public sealed class Crumble : IEffect
{
    private readonly CrumbleConfig _config;
    private readonly List<CharId> _pendingChars;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, Color> _characterFinalColorMap;
    private long _fallDelay;
    private long _maxFallDelay;
    private long _minFallDelay;
    private bool _reset;
    private long _fallGroupMaxsize;
    private CrumbleStage _stage;
    private List<CharId> _unvacuumedChars;

    public Crumble(CrumbleConfig config)
    {
        _config = config;
        _pendingChars = new List<CharId>();
        _characterFinalColorMap = new Dictionary<CharId, Color>();
        _fallDelay = 0;
        _maxFallDelay = 0;
        _minFallDelay = 0;
        _reset = false;
        _fallGroupMaxsize = 1;
        _stage = CrumbleStage.Falling;
        _unvacuumedChars = new List<CharId>();
    }

    public static Crumble FromOptions(Dictionary<string, object> options)
    {
        return new Crumble(new CrumbleConfig
        {
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
        // CrumbleIterator.DYNAMIC_NEUTRAL_GRAY
        Color dynamicNeutralGray = Color.FromHex("#808080");
        Color white = Color.FromHex("#ffffff");

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
        long canvasBottom = world.Terminal.Canvas.Bottom;
        long canvasTop = world.Terminal.Canvas.Top;
        long canvasCenterColumn = world.Terminal.Canvas.CenterColumn;
        long canvasCenterRow = world.Terminal.Canvas.CenterRow;
        foreach (CharId id in characters)
        {
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

            _characterFinalColorMap[id] = finalGradientMapping.Get(inputCoord)
                ?? throw new EngineInvariantException("gradient mapping missing");

            Color? weakFgColor;
            Color? weakBgColor;
            Color? dustFgColor;
            Color? dustBgColor;
            Gradient? strengthenFlashFgGradient;
            Gradient? strengthenFlashBgGradient;
            Gradient? strengthenFgGradient;
            Gradient? strengthenBgGradient;
            if (dynamic)
            {
                bool hasExistingColors = inputFg is not null || inputBg is not null;
                weakFgColor = inputFg is not null
                    ? Animation.AdjustColorBrightness(inputFg, 0.65)
                    : inputBg is null
                        ? Animation.AdjustColorBrightness(dynamicNeutralGray, 0.65)
                        : null;
                weakBgColor = inputBg is not null
                    ? Animation.AdjustColorBrightness(inputBg, 0.65)
                    : null;
                dustFgColor = inputFg is not null
                    ? Animation.AdjustColorBrightness(inputFg, 0.55)
                    : inputBg is null
                        ? Animation.AdjustColorBrightness(dynamicNeutralGray, 0.55)
                        : null;
                dustBgColor = inputBg is not null
                    ? Animation.AdjustColorBrightness(inputBg, 0.55)
                    : null;
                strengthenFlashFgGradient = inputFg is not null
                    ? Gradient.WithSteps([inputFg, white], 6, false)
                    : !hasExistingColors
                        ? Gradient.WithSteps([dynamicNeutralGray, white], 6, false)
                        : null;
                strengthenFlashBgGradient = inputBg is not null
                    ? Gradient.WithSteps([inputBg, white], 6, false)
                    : null;
                strengthenFgGradient = inputFg is not null
                    ? Gradient.WithSteps([white, inputFg], 9, false)
                    : null;
                strengthenBgGradient = inputBg is not null
                    ? Gradient.WithSteps([white, inputBg], 9, false)
                    : null;
            }
            else
            {
                Color finalColor = _characterFinalColorMap[id];
                weakFgColor = Animation.AdjustColorBrightness(finalColor, 0.65);
                weakBgColor = null;
                dustFgColor = Animation.AdjustColorBrightness(finalColor, 0.55);
                dustBgColor = null;
                strengthenFlashFgGradient = Gradient.WithSteps([finalColor, white], 6, false);
                strengthenFlashBgGradient = null;
                strengthenFgGradient = Gradient.WithSteps([white, finalColor], 9, false);
                strengthenBgGradient = null;
            }

            Gradient? weakenFgGradient = weakFgColor is not null && dustFgColor is not null
                ? Gradient.WithSteps([weakFgColor, dustFgColor], 9, false)
                : null;
            Gradient? weakenBgGradient = weakBgColor is not null && dustBgColor is not null
                ? Gradient.WithSteps([weakBgColor, dustBgColor], 9, false)
                : null;

            world.Terminal.SetCharacterVisibility(id, true);
            string initialScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                initialScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                ch.Animation.Scenes.Get(initialScn)!
                    .AddFrame(
                        inputSymbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(weakFgColor, weakBgColor) });
            }

            world.ActivateScene(this, id, initialScn);
            string fallPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                fallPath = ch.Motion.NewPath(0.65, Easing.OutBounce, null, 0, false, "");
                ch.Motion.Paths.Get(fallPath)!
                    .NewWaypoint(Coord.New(inputCoord.Column, canvasBottom), null, "");
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string weakenScn = ch.Animation.NewScene(false, null, null, "weaken", usesPre);
                ch.Animation.Scenes.Get(weakenScn)!
                    .ApplyGradientToSymbols(
                        [inputSymbol],
                        4,
                        weakenFgGradient,
                        weakenBgGradient);
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string topPath = ch.Motion.NewPath(1.0, Easing.OutQuint, null, 0, false, "top");
                ch.Motion.Paths.Get(topPath)!
                    .NewWaypoint(
                        Coord.New(inputCoord.Column, canvasTop),
                        [Coord.New(canvasCenterColumn, canvasCenterRow)],
                        "");
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string inputPath = ch.Motion.NewPath(1.0, null, null, 0, false, "input");
                ch.Motion.Paths.Get(inputPath)!
                    .NewWaypoint(inputCoord, null, "");
            }

            string strengthenFlashScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                strengthenFlashScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                ch.Animation.Scenes.Get(strengthenFlashScn)!
                    .ApplyGradientToSymbols(
                        [inputSymbol],
                        4,
                        strengthenFlashFgGradient,
                        strengthenFlashBgGradient);
            }

            string strengthenScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                strengthenScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                Scene scene = ch.Animation.Scenes.Get(strengthenScn)
                    ?? throw new EngineInvariantException("strengthen scene");
                if (dynamic && inputFg is null && inputBg is null)
                {
                    scene.AddFrame(
                        inputSymbol,
                        4,
                        new VisualParams { Colors = ColorPair.New(null, null) });
                }
                else
                {
                    scene.ApplyGradientToSymbols(
                        [inputSymbol],
                        4,
                        strengthenFgGradient,
                        strengthenBgGradient);
                }
            }

            string dustScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                dustScn = ch.Animation.NewScene(false, SyncMetric.Distance, null, "", usesPre);
            }

            string[] dustSymbols = ["*", ".", ","];
            for (int i = 0; i < 5; i++)
            {
                string symbol = world.Rng.Choice(dustSymbols);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.Scenes.Get(dustScn)!
                    .AddFrame(
                        symbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(dustFgColor, dustBgColor) });
            }

            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene("weaken"),
                new EventAction.ActivatePath(fallPath));
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene("weaken"),
                new EventAction.SetLayer(1));
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene("weaken"),
                new EventAction.ActivateScene(dustScn));
            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path("input"),
                new EventAction.ActivateScene(strengthenFlashScn));
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene(strengthenFlashScn),
                new EventAction.ActivateScene(strengthenScn));
            _pendingChars.Add(id);
        }

        world.Rng.Shuffle(_pendingChars);
        _fallDelay = 12;
        _maxFallDelay = 12;
        _minFallDelay = 9;
        _reset = false;
        _fallGroupMaxsize = 1;
        _stage = CrumbleStage.Falling;
        _unvacuumedChars = new List<CharId>(world.Terminal.InputCharacters);
        world.Rng.Shuffle(_unvacuumedChars);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_stage != CrumbleStage.Complete)
        {
            switch (_stage)
            {
                case CrumbleStage.Falling:
                    if (_pendingChars.Count > 0)
                    {
                        if (_fallDelay == 0)
                        {
                            long fallGroupSize = world.Rng.Randint(1, _fallGroupMaxsize);
                            for (long i = 0; i < fallGroupSize; i++)
                            {
                                if (_pendingChars.Count > 0)
                                {
                                    // crumble.rs:418 — FIFO remove(0); outer for still runs remaining iterations
                                    CharId nextChar = _pendingChars[0];
                                    _pendingChars.RemoveAt(0);
                                    world.ActivateScene(this, nextChar, "weaken");
                                    world.ActiveCharacters.Insert(
                                        nextChar,
                                        world.Terminal.Arena[(int)nextChar.Value].CharacterId);
                                }
                            }

                            _fallDelay = world.Rng.Randint(_minFallDelay, _maxFallDelay);
                            if (world.Rng.Randint(1, 10) > 4)
                            {
                                _fallGroupMaxsize += 1;
                                _minFallDelay = Math.Max(0, _minFallDelay - 1);
                                _maxFallDelay = Math.Max(0, _maxFallDelay - 1);
                            }
                        }
                        else
                        {
                            _fallDelay -= 1;
                        }
                    }

                    if (_pendingChars.Count == 0 && world.ActiveCharacters.IsEmpty)
                    {
                        _stage = CrumbleStage.Vacuuming;
                    }

                    break;
                case CrumbleStage.Vacuuming:
                    if (_unvacuumedChars.Count > 0)
                    {
                        long batch = world.Rng.Randint(3, 10);
                        for (long i = 0; i < batch; i++)
                        {
                            if (_unvacuumedChars.Count > 0)
                            {
                                // crumble.rs:443 — FIFO remove(0)
                                CharId nextChar = _unvacuumedChars[0];
                                _unvacuumedChars.RemoveAt(0);
                                world.ActivatePath(this, nextChar, "top");
                                world.ActiveCharacters.Insert(
                                    nextChar,
                                    world.Terminal.Arena[(int)nextChar.Value].CharacterId);
                            }
                        }
                    }

                    if (world.ActiveCharacters.IsEmpty)
                    {
                        _stage = CrumbleStage.Resetting;
                    }

                    break;
                case CrumbleStage.Resetting:
                    if (!_reset)
                    {
                        List<CharId> characters = world.Terminal.GetCharacters(
                            world.Rng,
                            CharacterFilter.Default,
                            CharacterSort.TopToBottomLeftToRight);
                        foreach (CharId id in characters)
                        {
                            world.ActivatePath(this, id, "input");
                            world.ActiveCharacters.Insert(
                                id,
                                world.Terminal.Arena[(int)id.Value].CharacterId);
                        }

                        _reset = true;
                    }

                    if (world.ActiveCharacters.IsEmpty)
                    {
                        _stage = CrumbleStage.Complete;
                    }

                    break;
                case CrumbleStage.Complete:
                    break;
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
