using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>vhstape, ported from effects/effect_vhstape.py. Transcribed from <c>effects/vhstape.rs</c>.</summary>
public sealed class VhsTapeConfig
{
    public List<Color> GlitchLineColors { get; set; } = new List<Color>();
    public List<Color> GlitchWaveColors { get; set; } = new List<Color>();
    public List<Color> NoiseColors { get; set; } = new List<Color>();
    public double GlitchLineChance { get; set; } = 0.05;
    public double NoiseChance { get; set; } = 0.004;
    public long TotalGlitchTime { get; set; } = 600;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public enum VhsTapePhase
{
    Glitching,
    Noise,
    Redraw,
    Complete,
}

public sealed class VhsTapeLine
{
    public List<CharId> Characters { get; }

    public VhsTapeLine(List<CharId> characters)
    {
        Characters = characters;
    }
}

public sealed class VhsTape : IEffect
{
    private readonly VhsTapeConfig _config;
    private List<VhsTapeLine> _lines = new List<VhsTapeLine>();
    private long? _activeGlitchWaveTop;
    private List<int> _activeGlitchWaveLines = new List<int>();
    private List<int> _activeGlitchLines = new List<int>();
    private readonly Dictionary<CharId, ColorPair> _characterStableColorMap = new Dictionary<CharId, ColorPair>();
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    private long _glitchingStepsElapsed;
    private VhsTapePhase _phase;
    private List<int> _toRedraw = new List<int>();
    private bool _redrawing;

    public VhsTape(VhsTapeConfig config)
    {
        _config = config;
        _activeGlitchWaveTop = null;
        _glitchingStepsElapsed = 0;
        _phase = VhsTapePhase.Glitching;
        _redrawing = false;
    }

    public static VhsTape FromOptions(Dictionary<string, object> options)
    {
        return new VhsTape(new VhsTapeConfig
        {
            GlitchLineColors = TypedList<Color>(options, "--glitch-line-colors"),
            GlitchWaveColors = TypedList<Color>(options, "--glitch-wave-colors"),
            NoiseColors = TypedList<Color>(options, "--noise-colors"),
            GlitchLineChance = (double)options["--glitch-line-chance"],
            NoiseChance = (double)options["--noise-chance"],
            TotalGlitchTime = (long)options["--total-glitch-time"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    private void BuildLineEffects(EngineWorld world, IReadOnlyList<CharId> characters)
    {
        List<Color> glitchLineColors = _config.GlitchLineColors;
        string[] snowChars = ["#", "*", ".", ":"];
        List<Color> noiseColors = _config.NoiseColors;
        long offset = world.Rng.Randint(4, 25);
        long direction = world.Rng.Choice([-1L, 1L]);
        long holdTime = world.Rng.Randint(1, 50);
        foreach (CharId id in characters)
        {
            Coord inputCoord;
            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
            }

            ColorPair stableColors = _characterStableColorMap[id];
            ColorPair finalColors = _characterFinalColorMap[id];
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string glitchPath = ch.Motion.NewPath(2.0, null, null, holdTime, false, "glitch");
                ch.Motion.Paths.Get(glitchPath)!
                    .NewWaypoint(
                        Coord.New(inputCoord.Column + offset * direction, inputCoord.Row),
                        null,
                        "glitch");
                string restorePath = ch.Motion.NewPath(2.0, null, null, 0, false, "restore");
                ch.Motion.Paths.Get(restorePath)!
                    .NewWaypoint(inputCoord, null, "restore");
                string midPath = ch.Motion.NewPath(2.0, null, null, 0, false, "glitch_wave_mid");
                ch.Motion.Paths.Get(midPath)!
                    .NewWaypoint(Coord.New(inputCoord.Column + 8, inputCoord.Row), null, "glitch_wave_mid");
                string endPath = ch.Motion.NewPath(2.0, null, null, 0, false, "glitch_wave_end");
                ch.Motion.Paths.Get(endPath)!
                    .NewWaypoint(Coord.New(inputCoord.Column + 14, inputCoord.Row), null, "glitch_wave_end");
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string baseScn = ch.Animation.NewScene(false, null, null, "base", usesPre);
                ch.Animation.Scenes.Get(baseScn)!
                    .AddFrame(inputSymbol, 1, new VisualParams { Colors = stableColors });
                string fwdScn = ch.Animation.NewScene(false, SyncMetric.Step, null, "rgb_glitch_fwd", usesPre);
                Scene fwdScene = ch.Animation.Scenes.Get(fwdScn)
                    ?? throw new EngineInvariantException("rgb_glitch_fwd scene");
                foreach (Color color in glitchLineColors)
                {
                    fwdScene.AddFrame(
                        inputSymbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }

                string bwdScn = ch.Animation.NewScene(false, SyncMetric.Step, null, "rgb_glitch_bwd", usesPre);
                Scene bwdScene = ch.Animation.Scenes.Get(bwdScn)
                    ?? throw new EngineInvariantException("rgb_glitch_bwd scene");
                // vhstape.rs:209 — .rev() order is behavior.
                for (int ci = glitchLineColors.Count - 1; ci >= 0; ci--)
                {
                    Color color = glitchLineColors[ci];
                    bwdScene.AddFrame(
                        inputSymbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }

                ch.Animation.NewScene(false, null, null, "snow", usesPre);
            }

            for (int i = 0; i < 25; i++)
            {
                string symbol = world.Rng.Choice(snowChars);
                Color color = world.Rng.Choice(noiseColors);
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("snow")!
                    .AddFrame(symbol, 2, new VisualParams { Colors = ColorPair.New(color, null) });
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.Scenes.Get("snow")!
                    .AddFrame(inputSymbol, 1, new VisualParams { Colors = stableColors });
                ch.Animation.NewScene(false, null, null, "final_snow", usesPre);
                string redrawScn = ch.Animation.NewScene(false, null, null, "final_redraw", usesPre);
                Scene redrawScene = ch.Animation.Scenes.Get(redrawScn)
                    ?? throw new EngineInvariantException("final_redraw scene");
                redrawScene.AddFrame(
                    "█",
                    6,
                    new VisualParams { Colors = ColorPair.New(Color.FromHex("ffffff"), null) });
                redrawScene.AddFrame(inputSymbol, 1, new VisualParams { Colors = finalColors });
            }

            for (int i = 0; i < 30; i++)
            {
                string symbol = world.Rng.Choice(snowChars);
                Color color = world.Rng.Choice(noiseColors);
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("final_snow")!
                    .AddFrame(symbol, 2, new VisualParams { Colors = ColorPair.New(color, null) });
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path("glitch"),
                new EventAction.ActivatePath("restore"));
            world.RegisterEvent(
                id,
                Event.PathActivated,
                new CallerKey.Path("glitch"),
                new EventAction.ActivateScene("rgb_glitch_fwd"));
            world.RegisterEvent(
                id,
                Event.PathActivated,
                new CallerKey.Path("restore"),
                new EventAction.ActivateScene("rgb_glitch_bwd"));
            world.RegisterEvent(
                id,
                Event.PathActivated,
                new CallerKey.Path("glitch_wave_mid"),
                new EventAction.ActivateScene("rgb_glitch_fwd"));
            world.RegisterEvent(
                id,
                Event.PathActivated,
                new CallerKey.Path("glitch_wave_end"),
                new EventAction.ActivateScene("rgb_glitch_fwd"));
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene("rgb_glitch_bwd"),
                new EventAction.ActivateScene("base"));
        }
    }

    private void LineSnow(EngineWorld world, int idx)
    {
        List<CharId> characters = _lines[idx].Characters;
        foreach (CharId id in characters)
        {
            world.ActivateScene(this, id, "snow");
        }
    }

    private void LineSetHoldTime(EngineWorld world, int idx, long holdTime)
    {
        foreach (CharId id in _lines[idx].Characters)
        {
            world.Terminal.Arena[(int)id.Value].Motion.Paths.Get("glitch")!.HoldTime = holdTime;
        }
    }

    private void LineGlitch(EngineWorld world, int idx, bool final_)
    {
        List<CharId> characters = _lines[idx].Characters;
        foreach (CharId id in characters)
        {
            if (final_)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.Paths.Get("glitch")!.HoldTime = 0;
                ch.Motion.Paths.Get("restore")!.HoldTime = 0;
            }

            double glitchSpeed = 40.0 / world.Rng.Randint(20, 40);
            double restoreSpeed = 40.0 / world.Rng.Randint(20, 40);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.Paths.Get("glitch")!.Speed = glitchSpeed;
                ch.Motion.Paths.Get("restore")!.Speed = restoreSpeed;
            }

            world.ActivatePath(this, id, "glitch");
        }
    }

    private void LineRestore(EngineWorld world, int idx)
    {
        List<CharId> characters = _lines[idx].Characters;
        foreach (CharId id in characters)
        {
            double restoreSpeed = 40.0 / world.Rng.Randint(20, 40);
            world.Terminal.Arena[(int)id.Value].Motion.Paths.Get("restore")!.Speed = restoreSpeed;
            world.ActivatePath(this, id, "restore");
        }
    }

    private void LineActivatePath(EngineWorld world, int idx, string pathId)
    {
        List<CharId> characters = _lines[idx].Characters;
        foreach (CharId id in characters)
        {
            world.ActivatePath(this, id, pathId);
        }
    }

    private bool LineMovementComplete(EngineWorld world, int idx)
    {
        foreach (CharId id in _lines[idx].Characters)
        {
            if (!world.Terminal.Arena[(int)id.Value].Motion.MovementIsComplete())
            {
                return false;
            }
        }

        return true;
    }

    private void InsertLineCharacters(EngineWorld world, int idx)
    {
        foreach (CharId id in _lines[idx].Characters)
        {
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }
    }

    private void GlitchWave(EngineWorld world)
    {
        if (_activeGlitchWaveTop is null or 0)
        {
            if (world.Terminal.Canvas.TextHeight >= 3)
            {
                long lower = System.Math.Max(3, PyCompat.RoundHalfEven(world.Terminal.Canvas.TextHeight * 0.5));
                _activeGlitchWaveTop = world.Terminal.Canvas.TextBottom
                    + world.Rng.Randint(lower, world.Terminal.Canvas.TextHeight);
            }
            else
            {
                return;
            }
        }

        bool allComplete = true;
        foreach (int idx in _activeGlitchWaveLines)
        {
            if (!LineMovementComplete(world, idx))
            {
                allComplete = false;
                break;
            }
        }

        if (allComplete)
        {
            if (_activeGlitchWaveLines.Count > 0)
            {
                bool shouldMove = world.Rng.Random() < 0.3;
                long waveTopDelta = shouldMove
                    ? world.Rng.Random() < 0.3 ? 1 : -1
                    : 0;
                long top = _activeGlitchWaveTop!.Value + waveTopDelta;
                top = System.Math.Max(2, System.Math.Min(top, world.Terminal.Canvas.TextTop));
                _activeGlitchWaveTop = top;
            }

            long waveTopValue = _activeGlitchWaveTop!.Value;
            var newWaveLines = new List<int>();
            for (long lineIndex = waveTopValue - 2; lineIndex <= waveTopValue; lineIndex++)
            {
                long adjustedLineIndex = lineIndex - (world.Terminal.Canvas.TextBottom - 1);
                if (adjustedLineIndex >= 0 && adjustedLineIndex < _lines.Count)
                {
                    newWaveLines.Add((int)adjustedLineIndex);
                }
            }

            List<int> oldWaveLines = _activeGlitchWaveLines;
            _activeGlitchWaveLines = new List<int>();
            foreach (int idx in oldWaveLines)
            {
                if (!newWaveLines.Contains(idx))
                {
                    LineRestore(world, idx);
                    InsertLineCharacters(world, idx);
                }
            }

            _activeGlitchWaveLines = newWaveLines;

            if (waveTopValue < world.Terminal.Canvas.TextBottom + 2)
            {
                List<int> waveLines = _activeGlitchWaveLines;
                _activeGlitchWaveLines = new List<int>();
                foreach (int idx in waveLines)
                {
                    LineRestore(world, idx);
                    InsertLineCharacters(world, idx);
                }

                _activeGlitchWaveTop = null;
            }
            else
            {
                string[] pathIds = ["glitch_wave_mid", "glitch_wave_end", "glitch_wave_mid"];
                List<int> waveLines = _activeGlitchWaveLines;
                for (int i = 0; i < waveLines.Count; i++)
                {
                    int idx = waveLines[i];
                    string pathId = pathIds[i];
                    LineActivatePath(world, idx, pathId);
                    InsertLineCharacters(world, idx);
                }
            }
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
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            if (dynamic)
            {
                Color? inputFg = ch.Animation.InputFgColor;
                Color? inputBg = ch.Animation.InputBgColor;
                Color? stableFg = inputFg ?? Color.FromHex("808080");
                _characterStableColorMap[id] = ColorPair.New(stableFg, inputBg);
                _characterFinalColorMap[id] = ColorPair.New(inputFg, inputBg);
            }
            else
            {
                Color gradientColor = finalGradientMapping.Get(ch.InputCoord)
                    ?? throw new EngineInvariantException("final gradient mapping missing coord");
                ColorPair stableColors = ColorPair.New(gradientColor, null);
                _characterStableColorMap[id] = stableColors;
                _characterFinalColorMap[id] = stableColors;
            }
        }

        List<List<CharId>> rows = world.Terminal.GetCharactersGrouped(
            CharacterFilter.Default,
            CharacterGroup.RowBottomToTop);
        foreach (List<CharId> row in rows)
        {
            BuildLineEffects(world, row);
            _lines.Add(new VhsTapeLine(row));
        }

        characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            world.Terminal.SetCharacterVisibility(id, true);
            world.ActivateScene(this, id, "base");
        }

        _glitchingStepsElapsed = 0;
        _phase = VhsTapePhase.Glitching;
        _toRedraw = new List<int>();
        for (int i = 0; i < _lines.Count; i++)
        {
            _toRedraw.Add(i);
        }

        _redrawing = false;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_phase == VhsTapePhase.Complete && world.ActiveCharacters.IsEmpty)
        {
            return null;
        }

        switch (_phase)
        {
            case VhsTapePhase.Glitching:
                if (_activeGlitchWaveLines.Count == 0
                    || _activeGlitchWaveLines.TrueForAll(idx => LineMovementComplete(world, idx)))
                {
                    GlitchWave(world);
                }

                List<int> glitchLines = _activeGlitchLines;
                _activeGlitchLines = new List<int>();
                foreach (int idx in glitchLines)
                {
                    if (!LineMovementComplete(world, idx))
                    {
                        _activeGlitchLines.Add(idx);
                    }
                }

                if (world.Rng.Random() < _config.GlitchLineChance && _activeGlitchLines.Count < 3)
                {
                    int glitchLine = world.Rng.ChoiceIndex(_lines.Count);
                    if (!_activeGlitchWaveLines.Contains(glitchLine)
                        && !_activeGlitchLines.Contains(glitchLine))
                    {
                        long holdTime = world.Rng.Randint(20, 75);
                        LineSetHoldTime(world, glitchLine, holdTime);
                        _activeGlitchLines.Add(glitchLine);
                        LineGlitch(world, glitchLine, false);
                        InsertLineCharacters(world, glitchLine);
                    }
                }

                if (world.Rng.Random() < _config.NoiseChance)
                {
                    for (int idx = 0; idx < _lines.Count; idx++)
                    {
                        LineSnow(world, idx);
                        if (!_activeGlitchWaveLines.Contains(idx)
                            && !_activeGlitchLines.Contains(idx))
                        {
                            InsertLineCharacters(world, idx);
                        }
                    }
                }

                _glitchingStepsElapsed += 1;
                if (_glitchingStepsElapsed >= _config.TotalGlitchTime)
                {
                    List<int> waveLines = _activeGlitchWaveLines;
                    foreach (int idx in waveLines)
                    {
                        LineRestore(world, idx);
                    }

                    List<int> activeGlitchLines = _activeGlitchLines;
                    foreach (int idx in activeGlitchLines)
                    {
                        LineRestore(world, idx);
                    }

                    _phase = VhsTapePhase.Noise;
                }

                break;
            case VhsTapePhase.Noise:
                if (world.ActiveCharacters.IsEmpty)
                {
                    List<CharId> characters = world.Terminal.GetCharacters(
                        world.Rng,
                        CharacterFilter.Default,
                        CharacterSort.TopToBottomLeftToRight);
                    foreach (CharId id in characters)
                    {
                        world.ActivateScene(this, id, "final_snow");
                        world.ActiveCharacters.Insert(
                            id,
                            world.Terminal.Arena[(int)id.Value].CharacterId);
                    }

                    _phase = VhsTapePhase.Redraw;
                }

                break;
            case VhsTapePhase.Redraw:
                if (_redrawing || world.ActiveCharacters.IsEmpty)
                {
                    _redrawing = true;
                    if (_toRedraw.Count > 0)
                    {
                        int nextLine = _toRedraw[^1];
                        _toRedraw.RemoveAt(_toRedraw.Count - 1);
                        List<CharId> characters = _lines[nextLine].Characters;
                        foreach (CharId id in characters)
                        {
                            world.ActivateScene(this, id, "final_redraw");
                            world.ActiveCharacters.Insert(
                                id,
                                world.Terminal.Arena[(int)id.Value].CharacterId);
                        }
                    }
                    else
                    {
                        _phase = VhsTapePhase.Complete;
                    }
                }

                break;
            case VhsTapePhase.Complete:
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
