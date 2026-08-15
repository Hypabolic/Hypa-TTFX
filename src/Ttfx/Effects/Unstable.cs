using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>unstable, ported from effects/effect_unstable.py. Transcribed from <c>effects/unstable.rs</c>.</summary>
public sealed class UnstableConfig
{
    public Color UnstableColor { get; set; } = Color.FromHex("ff9200");
    public Easing ExplosionEase { get; set; } = Easing.OutExpo;
    public double ExplosionSpeed { get; set; } = 1.0;
    public Easing ReassemblyEase { get; set; } = Easing.OutExpo;
    public double ReassemblySpeed { get; set; } = 1.0;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

internal enum UnstablePhase
{
    Rumble,
    Explosion,
    Reassembly,
}

public sealed class Unstable : IEffect
{
    private const string DynamicNeutralGrayHex = "808080";

    private readonly UnstableConfig _config;
    private readonly Dictionary<CharId, Coord> _jumbledCoords;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private readonly Dictionary<CharId, ColorPair> _characterStartColorMap;
    private long _explosionHoldTime;
    private UnstablePhase _phase;
    private long _maxRumbleSteps;
    private long _currentRumbleSteps;
    private long _rumbleModDelay;

    public Unstable(UnstableConfig config)
    {
        _config = config;
        _jumbledCoords = new Dictionary<CharId, Coord>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _characterStartColorMap = new Dictionary<CharId, ColorPair>();
        _explosionHoldTime = 30;
        _phase = UnstablePhase.Rumble;
        _maxRumbleSteps = 150;
        _currentRumbleSteps = 0;
        _rumbleModDelay = 18;
    }

    public static Unstable FromOptions(Dictionary<string, object> options)
    {
        return new Unstable(new UnstableConfig
        {
            UnstableColor = (Color)options["--unstable-color"],
            ExplosionEase = (Easing)options["--explosion-ease"],
            ExplosionSpeed = (double)options["--explosion-speed"],
            ReassemblyEase = (Easing)options["--reassembly-ease"],
            ReassemblySpeed = (double)options["--reassembly-speed"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    private static Color NeutralGray() => Color.FromHex(DynamicNeutralGrayHex);

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
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);

        foreach (CharId id in characters)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ColorPair startColors;
            ColorPair finalColors;
            if (dynamic)
            {
                Color startFgColor = ch.Animation.InputFgColor ?? NeutralGray();
                startColors = ColorPair.New(startFgColor, ch.Animation.InputBgColor);
                finalColors = ColorPair.New(ch.Animation.InputFgColor, ch.Animation.InputBgColor);
            }
            else
            {
                startColors = ColorPair.New(
                    finalGradientMapping.Get(ch.InputCoord)
                        ?? throw new EngineInvariantException("gradient mapping missing"),
                    null);
                finalColors = startColors;
            }

            _characterStartColorMap[id] = startColors;
            _characterFinalColorMap[id] = finalColors;
        }

        var characterCoords = new List<Coord>();
        foreach (CharId id in characters)
        {
            characterCoords.Add(world.Terminal.Arena[(int)id.Value].InputCoord);
        }

        foreach (CharId id in characters)
        {
            long pos = world.Rng.Randint(0, 3);
            long col;
            long row;
            switch (pos)
            {
                case 0:
                    col = world.Terminal.Canvas.Left;
                    row = world.Terminal.Canvas.RandomRow(world.Rng, false);
                    break;
                case 1:
                    col = world.Terminal.Canvas.Right;
                    row = world.Terminal.Canvas.RandomRow(world.Rng, false);
                    break;
                case 2:
                    col = world.Terminal.Canvas.RandomColumn(world.Rng, false);
                    row = world.Terminal.Canvas.Bottom;
                    break;
                default:
                    col = world.Terminal.Canvas.RandomColumn(world.Rng, false);
                    row = world.Terminal.Canvas.Top;
                    break;
            }

            // unstable.rs:167 — RNG-indexed remove
            int removeIndex = (int)world.Rng.Randint(0, characterCoords.Count - 1);
            Coord jumbledCoord = characterCoords[removeIndex];
            characterCoords.RemoveAt(removeIndex);
            _jumbledCoords[id] = jumbledCoord;

            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(jumbledCoord);
                string explosionPath = ch.Motion.NewPath(
                    _config.ExplosionSpeed,
                    _config.ExplosionEase,
                    null,
                    0,
                    false,
                    "explosion");
                ch.Motion.Paths.Get(explosionPath)!
                    .NewWaypoint(Coord.New(col, row), null, "");
                string reassemblyPath = ch.Motion.NewPath(
                    _config.ReassemblySpeed,
                    _config.ReassemblyEase,
                    null,
                    0,
                    false,
                    "reassembly");
                Coord inputCoord = ch.InputCoord;
                ch.Motion.Paths.Get(reassemblyPath)!
                    .NewWaypoint(inputCoord, null, "");
                ch.Animation.NewScene(false, null, null, "rumble", ch.UsesInputPreexistingColors);
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
            }

            if (dynamic)
            {
                ColorPair startPair = _characterStartColorMap[id];
                Color startFgColor = startPair.FgColor ?? NeutralGray();
                Color? startBgColor = startPair.BgColor;
                Gradient fgGradient = Gradient.WithSteps([startFgColor, _config.UnstableColor], 12, false);
                Gradient? bgGradient = startBgColor is not null
                    ? Gradient.WithSteps([startBgColor, _config.UnstableColor], 12, false)
                    : null;
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("rumble")!
                    .ApplyGradientToSymbols([inputSymbol], 10, fgGradient, bgGradient);
            }
            else
            {
                Color finalFgColor = _characterFinalColorMap[id].FgColor ?? NeutralGray();
                Gradient unstableGradient = Gradient.WithSteps([finalFgColor, _config.UnstableColor], 12, false);
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("rumble")!
                    .ApplyGradientToSymbols([inputSymbol], 10, unstableGradient, null);
            }

            world.Terminal.Arena[(int)id.Value].Animation.NewScene(false, null, null, "final", usesPre);
            if (dynamic)
            {
                ColorPair finalPair = _characterFinalColorMap[id];
                Color? finalFgColor = finalPair.FgColor;
                Color? finalBgColor = finalPair.BgColor;
                Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("final")
                    ?? throw new EngineInvariantException("final scene");
                if (finalFgColor is null && finalBgColor is null)
                {
                    Gradient fgGradient = Gradient.WithSteps([_config.UnstableColor, NeutralGray()], 12, false);
                    scene.ApplyGradientToSymbols([inputSymbol], 3, fgGradient, null);
                    scene.AddFrame(inputSymbol, 3, new VisualParams { Colors = new ColorPair() });
                }
                else
                {
                    Gradient? fgGradient = finalFgColor is not null
                        ? Gradient.WithSteps([_config.UnstableColor, finalFgColor], 12, false)
                        : null;
                    Gradient? bgGradient = finalBgColor is not null
                        ? Gradient.WithSteps([_config.UnstableColor, finalBgColor], 12, false)
                        : null;
                    scene.ApplyGradientToSymbols([inputSymbol], 3, fgGradient, bgGradient);
                    if (finalFgColor is null)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            3,
                            new VisualParams { Colors = ColorPair.New(null, finalBgColor) });
                    }
                }
            }
            else
            {
                Color finalFgColor = _characterFinalColorMap[id].FgColor ?? NeutralGray();
                Gradient finalColor = Gradient.WithSteps([_config.UnstableColor, finalFgColor], 12, false);
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("final")!
                    .ApplyGradientToSymbols([inputSymbol], 3, finalColor, null);
            }

            world.ActivateScene(this, id, "rumble");
            if (dynamic)
            {
                ColorPair startPair = _characterStartColorMap[id];
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.SetAppearance(ch.InputSymbol, ch.UsesInputPreexistingColors, ch.InputSymbol, startPair);
            }

            world.Terminal.SetCharacterVisibility(id, true);
        }

        _explosionHoldTime = 30;
        _phase = UnstablePhase.Rumble;
        _maxRumbleSteps = 150;
        _currentRumbleSteps = 0;
        _rumbleModDelay = 18;
    }

    public string? NextFrame(EngineWorld world)
    {
        string? nextFrame = null;
        if (_phase == UnstablePhase.Rumble)
        {
            if (_currentRumbleSteps < _maxRumbleSteps)
            {
                if (_currentRumbleSteps > 30 && _currentRumbleSteps % _rumbleModDelay == 0)
                {
                    long rowOffset = world.Rng.Choice([-1L, 0L, 1L]);
                    long columnOffset = world.Rng.Choice([-1L, 0L, 1L]);
                    List<CharId> characters = world.Terminal.GetCharacters(
                        world.Rng,
                        CharacterFilter.Default,
                        CharacterSort.TopToBottomLeftToRight);
                    foreach (CharId id in characters)
                    {
                        Motion motion = world.Terminal.Arena[(int)id.Value].Motion;
                        Coord current = motion.CurrentCoord;
                        motion.SetCoordinate(Coord.New(current.Column + columnOffset, current.Row + rowOffset));
                        world.StepAnimation(this, id);
                    }

                    nextFrame = world.Frame();
                    foreach (CharId id in characters)
                    {
                        Coord jumbled = _jumbledCoords[id];
                        world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(jumbled);
                    }

                    _rumbleModDelay -= 1;
                    _rumbleModDelay = Math.Max(_rumbleModDelay, 1);
                }
                else
                {
                    List<CharId> characters = world.Terminal.GetCharacters(
                        world.Rng,
                        CharacterFilter.Default,
                        CharacterSort.TopToBottomLeftToRight);
                    foreach (CharId id in characters)
                    {
                        world.StepAnimation(this, id);
                    }

                    nextFrame = world.Frame();
                }

                _currentRumbleSteps += 1;
            }
            else
            {
                _phase = UnstablePhase.Explosion;
                List<CharId> characters = world.Terminal.GetCharacters(
                    world.Rng,
                    CharacterFilter.Default,
                    CharacterSort.TopToBottomLeftToRight);
                foreach (CharId id in characters)
                {
                    world.ActivatePath(this, id, "explosion");
                }

                world.ActiveCharacters.Clear();
                foreach (CharId id in characters)
                {
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                }
            }
        }

        if (_phase == UnstablePhase.Explosion)
        {
            if (!world.ActiveCharacters.IsEmpty)
            {
                CharId[] snapshot = world.ActiveCharacters.Snapshot();
                foreach (CharId id in snapshot)
                {
                    world.Tick(this, id);
                }

                var retained = new List<CharId>();
                foreach (CharId id in world.ActiveCharacters.Snapshot())
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    Coord explosionTarget = ch.Motion.Paths.Get("explosion")!.Waypoints[0].Coord;
                    if (!ch.Motion.CurrentCoord.Equals(explosionTarget))
                    {
                        retained.Add(id);
                    }
                }

                world.ActiveCharacters.Clear();
                foreach (CharId id in retained)
                {
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                }

                nextFrame = world.Frame();
            }
            else if (_explosionHoldTime != 0)
            {
                _explosionHoldTime -= 1;
                nextFrame = world.Frame();
            }
            else
            {
                _phase = UnstablePhase.Reassembly;
                List<CharId> characters = world.Terminal.GetCharacters(
                    world.Rng,
                    CharacterFilter.Default,
                    CharacterSort.TopToBottomLeftToRight);
                foreach (CharId id in characters)
                {
                    world.ActivateScene(this, id, "final");
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                    world.ActivatePath(this, id, "reassembly");
                }
            }
        }

        if (_phase == UnstablePhase.Reassembly && !world.ActiveCharacters.IsEmpty)
        {
            CharId[] snapshot = world.ActiveCharacters.Snapshot();
            foreach (CharId id in snapshot)
            {
                world.Tick(this, id);
            }

            var retained = new List<CharId>();
            foreach (CharId id in world.ActiveCharacters.Snapshot())
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Coord reassemblyTarget = ch.Motion.Paths.Get("reassembly")!.Waypoints[0].Coord;
                if (!ch.Motion.CurrentCoord.Equals(reassemblyTarget) || !ch.Animation.ActiveSceneIsComplete())
                {
                    retained.Add(id);
                }
            }

            world.ActiveCharacters.Clear();
            foreach (CharId id in retained)
            {
                world.ActiveCharacters.Insert(
                    id,
                    world.Terminal.Arena[(int)id.Value].CharacterId);
            }

            nextFrame = world.Frame();
        }

        return nextFrame;
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
