using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>thunderstorm, ported from effects/effect_thunderstorm.py. Transcribed from <c>effects/thunderstorm.rs</c>.</summary>
public sealed class ThunderstormConfig
{
    public Color LightningColor { get; set; } = Color.FromHex("68A3E8");
    public Color GlowingTextColor { get; set; } = Color.FromHex("EF5411");
    public long TextGlowTime { get; set; } = 6;
    public List<string> RaindropSymbols { get; set; } = new List<string>();
    public List<string> SparkSymbols { get; set; } = new List<string>();
    public Color SparkGlowColor { get; set; } = Color.FromHex("ff4d00");
    public long SparkGlowTime { get; set; } = 18;
    public long StormTime { get; set; } = 12;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 3;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public enum ThunderstormPhase
{
    PreStorm,
    Waiting,
    Storm,
    Complete,
}

public sealed class Thunderstorm : IEffect
{
    /// <summary>fade_complete (effect_thunderstorm.py:388): phase -> storm, restart clock.</summary>
    private const uint CbFadeComplete = 0;

    private const uint CbHideCharacter = 1;
    private const uint CbMakeCharGlow = 2;
    private const uint CbReturnStrikeToPool = 3;
    private const uint CbSetStrikeInProgressFalse = 4;
    private const uint CbReclaimRain = 5;
    private const uint CbReclaimSpark = 6;

    private readonly ThunderstormConfig _config;
    private long _delay;
    private long _strikeProgressionDelay;
    private readonly ParticlePool _rainPool;
    private List<CharId> _pendingStrikeChars = new List<CharId>();
    private List<CharId> _availableStrikeChars = new List<CharId>();
    private List<CharId> _activeStrikeChars = new List<CharId>();
    private readonly ParticlePool _sparkPool;
    private Gradient _sparkGradient = Gradient.WithSteps([Color.FromHex("ff4d00")], 1, false);
    private List<CharId> _pendingGlowChars = new List<CharId>();
    private bool _strikeInProgress;
    private double _strikeBranchChance;
    private ThunderstormPhase _phase;
    private double _stormStartTime;

    public Thunderstorm(ThunderstormConfig config)
    {
        _config = config;
        _rainPool = ParticlePool.New(config.RaindropSymbols, null, null);
        _sparkPool = ParticlePool.New(config.SparkSymbols, 2000, null);
        _delay = 0;
        _strikeProgressionDelay = 0;
        _strikeInProgress = false;
        _strikeBranchChance = 0.05;
        _phase = ThunderstormPhase.PreStorm;
        _stormStartTime = 0.0;
    }

    public static Thunderstorm FromOptions(Dictionary<string, object> options)
    {
        return new Thunderstorm(new ThunderstormConfig
        {
            LightningColor = (Color)options["--lightning-color"],
            GlowingTextColor = (Color)options["--glowing-text-color"],
            TextGlowTime = (long)options["--text-glow-time"],
            RaindropSymbols = TypedList<string>(options, "--raindrop-symbols"),
            SparkSymbols = TypedList<string>(options, "--spark-symbols"),
            SparkGlowColor = (Color)options["--spark-glow-color"],
            SparkGlowTime = (long)options["--spark-glow-time"],
            StormTime = (long)options["--storm-time"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientFrames = (long)options["--final-gradient-frames"],
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    private static ColorPair AdjustColorPairBrightness(ColorPair colors, double brightness)
    {
        return ColorPair.New(
            colors.FgColor is not null ? Animation.AdjustColorBrightness(colors.FgColor, brightness) : null,
            colors.BgColor is not null ? Animation.AdjustColorBrightness(colors.BgColor, brightness) : null);
    }

    /// <summary>
    /// ThunderstormIterator._add_color_pair_gradient_frames. Faithful quirk: when
    /// both endpoint colors exist the gradient list has steps+1 entries but only
    /// range(steps) of them are emitted as frames.
    /// </summary>
    private static void AddColorPairGradientFrames(
        Scene scene,
        string symbol,
        ColorPair startColors,
        ColorPair endColors,
        long steps,
        long duration)
    {
        List<Color?> fgSteps;
        if (startColors.FgColor is not null && endColors.FgColor is not null)
        {
            fgSteps = new List<Color?>();
            foreach (Color color in Gradient.WithSteps(
                         [startColors.FgColor, endColors.FgColor],
                         steps,
                         false).Spectrum)
            {
                fgSteps.Add(color);
            }
        }
        else
        {
            Color? filler = endColors.FgColor ?? startColors.FgColor;
            fgSteps = new List<Color?>();
            for (int i = 0; i < steps; i++)
            {
                fgSteps.Add(filler);
            }
        }

        List<Color?> bgSteps;
        if (startColors.BgColor is not null && endColors.BgColor is not null)
        {
            bgSteps = new List<Color?>();
            foreach (Color color in Gradient.WithSteps(
                         [startColors.BgColor, endColors.BgColor],
                         steps,
                         false).Spectrum)
            {
                bgSteps.Add(color);
            }
        }
        else
        {
            Color? filler = endColors.BgColor ?? startColors.BgColor;
            bgSteps = new List<Color?>();
            for (int i = 0; i < steps; i++)
            {
                bgSteps.Add(filler);
            }
        }

        for (int index = 0; index < steps; index++)
        {
            scene.AddFrame(
                symbol,
                duration,
                new VisualParams { Colors = ColorPair.New(fgSteps[index], bgSteps[index]) });
        }
    }

    private static void InitializeRaindrop(EngineWorld world, CharId id)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Layer = 1;
        string inputSymbol = ch.InputSymbol;
        bool usesPre = ch.UsesInputPreexistingColors;
        ch.Animation.SetAppearance(
            inputSymbol,
            usesPre,
            inputSymbol,
            ColorPair.New(Color.FromHex("aaaaff"), null));
    }

    private static void InitializeSpark(
        EngineWorld world,
        CharId id,
        Gradient sparkGradient,
        long sparkGlowTime)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Layer = 2;
        string inputSymbol = ch.InputSymbol;
        bool usesPre = ch.UsesInputPreexistingColors;
        string sparkScn = ch.Animation.NewScene(false, null, Easing.InCirc, "glow", usesPre);
        Scene scene = ch.Animation.Scenes.Get(sparkScn)
            ?? throw new EngineInvariantException("spark glow scene");
        foreach (Color color in sparkGradient.Spectrum)
        {
            scene.AddFrame(
                inputSymbol,
                sparkGlowTime,
                new VisualParams { Colors = ColorPair.New(color, null) });
        }
    }

    private static void SetupRaindrop(EngineWorld world, CharId id)
    {
        Coord origin = world.Terminal.Arena[(int)id.Value].Motion.CurrentCoord;
        double speed = world.Rng.Uniform(0.5, 1.5);
        long canvasTop = world.Terminal.Canvas.Top;
        long canvasBottom = world.Terminal.Canvas.Bottom;
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        string fallPath = ch.Motion.NewPath(speed, null, null, 0, false, "");
        ch.Motion.Paths.Get(fallPath)!
            .NewWaypoint(Coord.New(origin.Column + canvasTop + 1, canvasBottom - 1), null, "");
        world.RegisterEvent(
            id,
            Event.PathComplete,
            new CallerKey.Path(fallPath),
            new EventAction.Callback(new EffectCallback(CbReclaimRain, [])));
        world.ActivatePath(NoopHooks.Instance, id, fallPath);
    }

    private static void SetupSparksForImpact(EngineWorld world, CharId id)
    {
        Coord impactCoord = world.Terminal.Arena[(int)id.Value].Motion.CurrentCoord;
        double speed = world.Rng.Uniform(0.1, 0.25);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        string sparkPath = ch.Motion.NewPath(speed, Easing.OutQuint, null, 30, false, "");
        long offset = world.Rng.Randint(4, 20) * world.Rng.Choice([1L, -1L]);
        Coord sparkTarget = Coord.New(impactCoord.Column + offset, world.Terminal.Canvas.Bottom);
        long bezierColumn = impactCoord.Column - PyCompat.FloorDiv(impactCoord.Column - sparkTarget.Column, 2);
        long bezierRow = world.Rng.Randint(1, world.Terminal.Canvas.Top);
        ch.Motion.Paths.Get(sparkPath)!
            .NewWaypoint(
                sparkTarget,
                [Coord.New(bezierColumn, bezierRow)],
                "");
        world.RegisterEvent(
            id,
            Event.SceneComplete,
            new CallerKey.Scene("glow"),
            new EventAction.Callback(new EffectCallback(CbReclaimSpark, [])));
        world.ActivateScene(NoopHooks.Instance, id, "glow");
        world.ActivatePath(NoopHooks.Instance, id, sparkPath);
    }

    private void BuildStrikeCharacters(EngineWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            CharId strikeChar = world.Terminal.AddCharacter("|", Coord.New(1, 1));
            _availableStrikeChars.Add(strikeChar);
        }
    }

    private CharId GetNextStrikeChar(EngineWorld world)
    {
        if (_availableStrikeChars.Count == 0)
        {
            BuildStrikeCharacters(world, 20);
        }

        int last = _availableStrikeChars.Count - 1;
        CharId strikeChar = _availableStrikeChars[last];
        _availableStrikeChars.RemoveAt(last);
        EffectCharacter ch = world.Terminal.Arena[(int)strikeChar.Value];
        ch.Animation.Scenes.Clear();
        ch.EventHandler.Clear();
        return strikeChar;
    }

    private void SetupLightningStrike(EngineWorld world, CharId? branchNeighbor)
    {
        long column;
        long row;
        if (branchNeighbor is CharId neighbor)
        {
            Coord coord = world.Terminal.Arena[(int)neighbor.Value].Motion.CurrentCoord;
            column = coord.Column;
            row = coord.Row;
        }
        else
        {
            column = world.Rng.Randint(1, world.Terminal.Canvas.Right);
            row = world.Terminal.Canvas.Top;
        }

        while (row >= world.Terminal.Canvas.Bottom)
        {
            if (_availableStrikeChars.Count == 0)
            {
                BuildStrikeCharacters(world, 20);
            }

            string symbol;
            if (branchNeighbor is CharId branch)
            {
                string neighborSymbol = world.Terminal.Arena[(int)branch.Value].InputSymbol;
                if (neighborSymbol == "/")
                {
                    column += 1;
                    symbol = world.Rng.Choice(["|", "\\"]);
                }
                else if (neighborSymbol == "\\")
                {
                    column -= 1;
                    symbol = world.Rng.Choice(["|", "/"]);
                }
                else
                {
                    long delta = world.Rng.Choice([-1L, 1L]);
                    column += delta;
                    symbol = delta == 1 ? "\\" : "/";
                }
            }
            else
            {
                symbol = world.Rng.Choice(["\\", "/", "|"]);
            }

            CharId strikeChar = GetNextStrikeChar(world);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)strikeChar.Value];
                ch.Motion.SetCoordinate(Coord.New(column, row));
                string inputSymbol = ch.InputSymbol;
                bool usesPre = ch.UsesInputPreexistingColors;
                ch.Animation.SetAppearance(
                    inputSymbol,
                    usesPre,
                    symbol,
                    ColorPair.New(_config.LightningColor, null));
            }

            row -= 1;
            if (symbol == "\\")
            {
                column += 1;
            }
            else if (symbol == "/")
            {
                column -= 1;
            }

            _pendingStrikeChars.Add(strikeChar);
            if (world.Rng.Random() < _strikeBranchChance && branchNeighbor is null)
            {
                _strikeBranchChance -= 0.01;
                SetupLightningStrike(world, strikeChar);
            }

            branchNeighbor = null;
        }

        _strikeBranchChance = 0.05;
    }

    private void LightningStrike(EngineWorld world)
    {
        SetupLightningStrike(world, null);
        Color strikeBaseColor = _config.LightningColor;
        Color strikeFlashColor = Animation.AdjustColorBrightness(strikeBaseColor, 1.7);
        Gradient strikeGradient = Gradient.WithSteps([strikeBaseColor, strikeFlashColor], 7, true);
        Gradient fadeGradient = Gradient.WithSteps(
            [strikeBaseColor, world.Terminal.Config.TerminalBackgroundColor],
            6,
            false);
        long layer = 1;
        Easing flashEase = Easing.CubicBezier(0.0, 1.6, 1.0, world.Rng.Uniform(-0.6, 0.4));
        foreach (CharId strikeChar in _pendingStrikeChars)
        {
            string symbol = world.Terminal.Arena[(int)strikeChar.Value].Animation.CurrentCharacterVisual.Symbol;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)strikeChar.Value];
                bool usesPre = ch.UsesInputPreexistingColors;
                string flashScn = ch.Animation.NewScene(false, null, flashEase, "flash", usesPre);
                Scene flashScene = ch.Animation.Scenes.Get(flashScn)
                    ?? throw new EngineInvariantException("flash scene");
                foreach (Color color in strikeGradient.Spectrum)
                {
                    flashScene.AddFrame(
                        symbol,
                        6,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }

                string fadeScn = ch.Animation.NewScene(false, null, null, "fade", usesPre);
                Scene fadeScene = ch.Animation.Scenes.Get(fadeScn)
                    ?? throw new EngineInvariantException("fade scene");
                foreach (Color color in fadeGradient.Spectrum)
                {
                    fadeScene.AddFrame(
                        symbol,
                        2,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }

                ch.Layer = layer;
            }

            world.RegisterEvent(
                strikeChar,
                Event.SceneComplete,
                new CallerKey.Scene("flash"),
                new EventAction.ActivateScene("fade"));
            world.RegisterEvent(
                strikeChar,
                Event.SceneComplete,
                new CallerKey.Scene("fade"),
                new EventAction.Callback(new EffectCallback(CbHideCharacter, [])));
            world.RegisterEvent(
                strikeChar,
                Event.SceneComplete,
                new CallerKey.Scene("fade"),
                new EventAction.Callback(new EffectCallback(CbMakeCharGlow, [])));
            world.RegisterEvent(
                strikeChar,
                Event.SceneComplete,
                new CallerKey.Scene("fade"),
                new EventAction.Callback(new EffectCallback(CbReturnStrikeToPool, [])));
        }

        List<CharId> textChars = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in textChars)
        {
            world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("flash")!.Ease = flashEase;
        }
    }

    private void StepLightningStrike(EngineWorld world)
    {
        if (_strikeProgressionDelay != 0)
        {
            _strikeProgressionDelay -= 1;
            return;
        }

        if (_pendingStrikeChars.Count > 0)
        {
            long batch = world.Rng.Randint(1, 3);
            for (long i = 0; i < batch; i++)
            {
                if (_pendingStrikeChars.Count == 0)
                {
                    break;
                }

                // thunderstorm.rs:513 — remove(0) FIFO.
                CharId nextStrikeChar = _pendingStrikeChars[0];
                _pendingStrikeChars.RemoveAt(0);
                _activeStrikeChars.Add(nextStrikeChar);
                world.Terminal.SetCharacterVisibility(nextStrikeChar, true);
                _strikeProgressionDelay = 1;

                if (_pendingStrikeChars.Count == 0)
                {
                    Gradient sparkGradient = _sparkGradient;
                    long sparkGlowTime = _config.SparkGlowTime;
                    long sparkCount = world.Rng.Randint(12, 18);
                    ParticleReset reset = ParticleReset.Default with { ClearEvents = true };
                    for (long s = 0; s < sparkCount; s++)
                    {
                        Coord origin = world.Terminal.Arena[(int)_activeStrikeChars[^1].Value].Motion.CurrentCoord;
                        _sparkPool.Emit(
                            world,
                            origin,
                            null,
                            true,
                            reset,
                            (w, particle) => InitializeSpark(w, particle, sparkGradient, sparkGlowTime),
                            SetupSparksForImpact);
                    }

                    world.RegisterEvent(
                        nextStrikeChar,
                        Event.SceneComplete,
                        new CallerKey.Scene("fade"),
                        new EventAction.Callback(new EffectCallback(CbSetStrikeInProgressFalse, [])));

                    List<CharId> strikes = _activeStrikeChars;
                    _activeStrikeChars = new List<CharId>();
                    foreach (CharId strikeChar in strikes)
                    {
                        world.ActivateScene(this, strikeChar, "flash");
                        world.ActiveCharacters.Insert(
                            strikeChar,
                            world.Terminal.Arena[(int)strikeChar.Value].CharacterId);
                    }

                    List<CharId> textChars = world.Terminal.GetCharacters(
                        world.Rng,
                        CharacterFilter.Default,
                        CharacterSort.TopToBottomLeftToRight);
                    foreach (CharId id in textChars)
                    {
                        world.ActivateScene(this, id, "flash");
                        world.ActiveCharacters.Insert(
                            id,
                            world.Terminal.Arena[(int)id.Value].CharacterId);
                    }
                }
            }
        }
    }

    private void Rain(EngineWorld world)
    {
        if (_delay != 0)
        {
            _delay -= 1;
            return;
        }

        long count = world.Rng.Randint(1, 6);
        ParticleReset reset = ParticleReset.Default with { ClearEvents = true };
        for (long i = 0; i < count; i++)
        {
            long spawnColumn = world.Rng.Randint(1 - world.Terminal.Canvas.Top, world.Terminal.Canvas.Right);
            Coord origin = Coord.New(spawnColumn - 1, world.Terminal.Canvas.Top + 1);
            _rainPool.Emit(
                world,
                origin,
                null,
                true,
                reset,
                InitializeRaindrop,
                SetupRaindrop);
        }

        _delay = world.Rng.Randint(1, 7);
    }

    private void PreStormTextFade(EngineWorld world)
    {
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            world.ActivateScene(this, id, "fade");
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }
    }

    private void PostStormTextFadeIn(EngineWorld world)
    {
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            world.ActivateScene(this, id, "unfade");
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
        switch (callback.Id)
        {
            case CbFadeComplete:
                _phase = ThunderstormPhase.Storm;
                _stormStartTime = world.Clock.NowMonotonic();
                break;
            case CbHideCharacter:
                world.Terminal.SetCharacterVisibility(character, false);
                break;
            case CbMakeCharGlow:
            {
                Coord coord = world.Terminal.Arena[(int)character.Value].Motion.CurrentCoord;
                CharId? inputChar = world.Terminal.GetCharacterByInputCoord(coord);
                if (inputChar is CharId ic
                    && world.Terminal.Arena[(int)ic.Value].IsVisible)
                {
                    world.ActivateScene(this, ic, "glow");
                    _pendingGlowChars.Add(ic);
                }

                break;
            }
            case CbReturnStrikeToPool:
                _availableStrikeChars.Add(character);
                break;
            case CbSetStrikeInProgressFalse:
                _strikeInProgress = false;
                break;
            case CbReclaimRain:
                _rainPool.Reclaim(world, character, true, true);
                break;
            case CbReclaimSpark:
                _sparkPool.Reclaim(world, character, true, true);
                break;
        }
    }

    public void Build(EngineWorld world)
    {
        _rainPool.Preallocate(world, 50, InitializeRaindrop);
        _sparkGradient = Gradient.WithSteps(
            [_config.SparkGlowColor, world.Terminal.Config.TerminalBackgroundColor],
            7,
            false);
        Gradient sparkGradient = _sparkGradient;
        long sparkGlowTime = _config.SparkGlowTime;
        _sparkPool.Preallocate(
            world,
            200,
            (w, particle) => InitializeSpark(w, particle, sparkGradient, sparkGlowTime));
        _stormStartTime = world.Clock.NowMonotonic();

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
        BuildStrikeCharacters(world, 200);

        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        Color dynamicNeutralGray = Color.FromHex("808080");
        List<CharId> allChars = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in allChars)
        {
            string inputSymbol;
            Coord inputCoord;
            bool usesPre;
            Color? inputFg;
            Color? inputBg;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputSymbol = ch.InputSymbol;
                inputCoord = ch.InputCoord;
                usesPre = ch.UsesInputPreexistingColors;
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
            }

            ColorPair visibleColors;
            ColorPair restoreColors;
            if (dynamic)
            {
                visibleColors = ColorPair.New(
                    inputFg ?? dynamicNeutralGray,
                    inputBg);
                restoreColors = ColorPair.New(inputFg, inputBg);
            }
            else
            {
                Color visibleFg = finalGradientMapping.Get(inputCoord)
                    ?? throw new EngineInvariantException("final gradient mapping missing coord");
                visibleColors = ColorPair.New(visibleFg, null);
                restoreColors = visibleColors;
            }

            ColorPair stormColors = AdjustColorPairBrightness(visibleColors, 0.5);
            Gradient glowFgGradient = Gradient.WithSteps(
                [_config.GlowingTextColor, stormColors.FgColor ?? throw new EngineInvariantException("storm fg")],
                7,
                false);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string glowScn = ch.Animation.NewScene(false, null, null, "glow", usesPre);
                Scene glowScene = ch.Animation.Scenes.Get(glowScn)
                    ?? throw new EngineInvariantException("glow scene");
                foreach (Color color in glowFgGradient.Spectrum)
                {
                    glowScene.AddFrame(
                        inputSymbol,
                        _config.TextGlowTime,
                        new VisualParams { Colors = ColorPair.New(color, stormColors.BgColor) });
                }

                if (dynamic)
                {
                    glowScene.AddFrame(
                        inputSymbol,
                        _config.TextGlowTime,
                        new VisualParams { Colors = stormColors });
                }
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string fadeScn = ch.Animation.NewScene(false, null, null, "fade", usesPre);
                Scene fadeScene = ch.Animation.Scenes.Get(fadeScn)
                    ?? throw new EngineInvariantException("fade scene");
                if (dynamic)
                {
                    AddColorPairGradientFrames(fadeScene, inputSymbol, visibleColors, stormColors, 7, 12);
                    fadeScene.AddFrame(inputSymbol, 12, new VisualParams { Colors = stormColors });
                }
                else
                {
                    Gradient fadeGradient = Gradient.WithSteps(
                        [visibleColors.FgColor ?? throw new EngineInvariantException("visible fg"), stormColors.FgColor ?? throw new EngineInvariantException("storm fg")],
                        7,
                        false);
                    foreach (Color color in fadeGradient.Spectrum)
                    {
                        fadeScene.AddFrame(
                            inputSymbol,
                            12,
                            new VisualParams { Colors = ColorPair.New(color, null) });
                    }
                }
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string unfadeScn = ch.Animation.NewScene(false, null, null, "unfade", usesPre);
                Scene unfadeScene = ch.Animation.Scenes.Get(unfadeScn)
                    ?? throw new EngineInvariantException("unfade scene");
                if (dynamic)
                {
                    AddColorPairGradientFrames(unfadeScene, inputSymbol, stormColors, visibleColors, 7, 12);
                    unfadeScene.AddFrame(inputSymbol, 12, new VisualParams { Colors = visibleColors });
                    if (!restoreColors.Equals(visibleColors))
                    {
                        unfadeScene.AddFrame(inputSymbol, 12, new VisualParams { Colors = restoreColors });
                    }
                }
                else
                {
                    List<Color> unfadeGradient = new List<Color>(
                        Gradient.WithSteps(
                            [visibleColors.FgColor ?? throw new EngineInvariantException("visible fg"), stormColors.FgColor ?? throw new EngineInvariantException("storm fg")],
                            7,
                            false).Spectrum);
                    unfadeGradient.Reverse();
                    foreach (Color color in unfadeGradient)
                    {
                        unfadeScene.AddFrame(
                            inputSymbol,
                            12,
                            new VisualParams { Colors = ColorPair.New(color, null) });
                    }
                }
            }

            Color lightningFlashColor = Animation.AdjustColorBrightness(
                visibleColors.FgColor ?? throw new EngineInvariantException("visible fg"),
                1.7);
            Gradient flashGradient = Gradient.WithSteps(
                [stormColors.FgColor ?? throw new EngineInvariantException("storm fg"), lightningFlashColor],
                7,
                true);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string strikeScn = ch.Animation.NewScene(false, null, null, "flash", usesPre);
                Scene strikeScene = ch.Animation.Scenes.Get(strikeScn)
                    ?? throw new EngineInvariantException("flash scene");
                foreach (Color color in flashGradient.Spectrum)
                {
                    strikeScene.AddFrame(
                        inputSymbol,
                        6,
                        new VisualParams { Colors = ColorPair.New(color, stormColors.BgColor) });
                }
            }

            world.Terminal.SetCharacterVisibility(id, true);
        }

        CharId referenceChar = allChars[0];
        world.RegisterEvent(
            referenceChar,
            Event.SceneComplete,
            new CallerKey.Scene("fade"),
            new EventAction.Callback(new EffectCallback(CbFadeComplete, [])));
    }

    public string? NextFrame(EngineWorld world)
    {
        if (world.ActiveCharacters.IsEmpty && _phase == ThunderstormPhase.Complete)
        {
            return null;
        }

        switch (_phase)
        {
            case ThunderstormPhase.PreStorm:
                PreStormTextFade(world);
                _phase = ThunderstormPhase.Waiting;
                break;
            case ThunderstormPhase.Storm:
                Rain(world);
                if (!_strikeInProgress && world.Rng.Random() < 0.008)
                {
                    _strikeInProgress = true;
                    LightningStrike(world);
                }

                if (_strikeInProgress)
                {
                    StepLightningStrike(world);
                }

                foreach (CharId glowChar in _pendingGlowChars)
                {
                    world.ActiveCharacters.Insert(
                        glowChar,
                        world.Terminal.Arena[(int)glowChar.Value].CharacterId);
                }

                _pendingGlowChars.Clear();
                if (world.Clock.NowMonotonic() - _stormStartTime >= _config.StormTime
                    && !_strikeInProgress)
                {
                    PostStormTextFadeIn(world);
                    _phase = ThunderstormPhase.Complete;
                }

                break;
            case ThunderstormPhase.Waiting:
            case ThunderstormPhase.Complete:
                break;
        }

        world.Update(this);
        return world.Frame();
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
