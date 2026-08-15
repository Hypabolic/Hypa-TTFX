using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>matrix, ported from effects/effect_matrix.py. Transcribed from <c>effects/matrix.rs</c>.</summary>
public sealed class MatrixConfig
{
    public Color HighlightColor { get; set; } = Color.FromHex("dbffdb");
    public List<Color> RainColorGradient { get; set; } = new List<Color>();
    public List<string> RainSymbols { get; set; } = new List<string>();
    public (long, long) RainFallDelayRange { get; set; } = (2, 15);
    public (long, long) RainColumnDelayRange { get; set; } = (3, 9);
    public long RainTime { get; set; } = 15;
    public double SymbolSwapChance { get; set; } = 0.005;
    public double ColorSwapChance { get; set; } = 0.001;
    public long ResolveDelay { get; set; } = 3;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 3;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Radial;
}

public enum MatrixColumnPhase
{
    Rain,
    Fill,
}

public enum MatrixPhase
{
    Rain,
    Fill,
    Resolve,
}

/// <summary>MatrixIterator.RainColumn.</summary>
public sealed class RainColumn
{
    public List<CharId> Characters { get; }
    public List<CharId> PendingCharacters { get; } = new List<CharId>();
    public List<CharId> VisibleCharacters { get; } = new List<CharId>();
    public MatrixColumnPhase Phase { get; private set; }
    public double ColumnDropChance { get; set; } = 0.08;
    public long BaseRainFallDelay { get; private set; }
    public long ActiveRainFallDelay { get; private set; }
    public int Length { get; private set; }
    public long HoldTime { get; set; }

    public RainColumn(EngineWorld world, MatrixConfig config, List<CharId> characters)
    {
        Characters = characters;
        SetupColumn(world, config, MatrixColumnPhase.Rain);
    }

    public void SetupColumn(EngineWorld world, MatrixConfig config, MatrixColumnPhase phase)
    {
        PendingCharacters.Clear();
        Phase = phase;
        foreach (CharId character in Characters)
        {
            world.Terminal.SetCharacterVisibility(character, false);
            PendingCharacters.Add(character);
            world.Terminal.Arena[(int)character.Value].Motion.CurrentCoord =
                world.Terminal.Arena[(int)character.Value].InputCoord;
        }

        VisibleCharacters.Clear();
        if (Phase == MatrixColumnPhase.Fill)
        {
            BaseRainFallDelay = world.Rng.Randint(
                System.Math.Max(PyCompat.FloorDiv(config.RainFallDelayRange.Item1, 3), 1),
                System.Math.Max(PyCompat.FloorDiv(config.RainFallDelayRange.Item2, 3), 1));
        }
        else
        {
            BaseRainFallDelay = world.Rng.Randint(
                config.RainFallDelayRange.Item1,
                config.RainFallDelayRange.Item2);
        }

        ActiveRainFallDelay = 0;
        if (Phase == MatrixColumnPhase.Rain)
        {
            Length = (int)world.Rng.Randint(
                System.Math.Max(1, PyCompat.TruncToI64(Characters.Count * 0.1)),
                Characters.Count);
        }
        else
        {
            Length = Characters.Count;
        }

        HoldTime = 0;
        if (Length == Characters.Count)
        {
            HoldTime = world.Rng.Randint(20, 45);
        }
    }

    public void TrimColumn(EngineWorld world, IReadOnlyList<Color> rainColors)
    {
        if (VisibleCharacters.Count == 0)
        {
            return;
        }

        CharId poppedChar = VisibleCharacters[0];
        VisibleCharacters.RemoveAt(0);
        world.Terminal.SetCharacterVisibility(poppedChar, false);
        if (VisibleCharacters.Count > 1)
        {
            FadeLastCharacter(world, rainColors);
        }
    }

    public void DropColumn(EngineWorld world)
    {
        long canvasBottom = world.Terminal.Canvas.Bottom;
        var outOfCanvas = new List<CharId>();
        foreach (CharId character in VisibleCharacters)
        {
            Motion motion = world.Terminal.Arena[(int)character.Value].Motion;
            Coord current = motion.CurrentCoord;
            motion.CurrentCoord = Coord.New(current.Column, current.Row - 1);
            Coord newCoord = motion.CurrentCoord;
            if (newCoord.Row < canvasBottom)
            {
                world.Terminal.SetCharacterVisibility(character, false);
                outOfCanvas.Add(character);
            }
        }

        VisibleCharacters.RemoveAll(ch => outOfCanvas.Contains(ch));
    }

    public void FadeLastCharacter(EngineWorld world, IReadOnlyList<Color> rainColors)
    {
        int tailStart = System.Math.Max(0, rainColors.Count - 3);
        var tail = new List<Color>();
        for (int i = tailStart; i < rainColors.Count; i++)
        {
            tail.Add(rainColors[i]);
        }

        Color darkerColor = Animation.AdjustColorBrightness(world.Rng.Choice(tail), 0.65);
        CharId target = VisibleCharacters[0];
        string symbol = world.Terminal.Arena[(int)target.Value].Animation.CurrentCharacterVisual.Symbol;
        Matrix.SetAppearance(world, target, symbol, ColorPair.New(darkerColor, null));
    }

    public CharId ResolveChar(EngineWorld world)
    {
        int index = (int)world.Rng.Randint(0, VisibleCharacters.Count - 1);
        CharId resolved = VisibleCharacters[index];
        VisibleCharacters.RemoveAt(index);
        return resolved;
    }

    public void Tick(EngineWorld world, MatrixConfig config, IReadOnlyList<Color> rainColors)
    {
        if (ActiveRainFallDelay == 0)
        {
            if (PendingCharacters.Count > 0)
            {
                CharId nextChar = PendingCharacters[0];
                PendingCharacters.RemoveAt(0);
                string symbol = world.Rng.Choice(config.RainSymbols);
                Matrix.SetAppearance(
                    world,
                    nextChar,
                    symbol,
                    ColorPair.New(config.HighlightColor, null));
                CharId? previousCharacter = VisibleCharacters.Count > 0
                    ? VisibleCharacters[^1]
                    : null;
                if (previousCharacter is CharId prev)
                {
                    string prevSymbol = world.Terminal.Arena[(int)prev.Value].Animation
                        .CurrentCharacterVisual.Symbol;
                    Color fg = world.Rng.Choice(rainColors);
                    Matrix.SetAppearance(world, prev, prevSymbol, ColorPair.New(fg, null));
                }

                world.Terminal.SetCharacterVisibility(nextChar, true);
                VisibleCharacters.Add(nextChar);
            }
            else if (VisibleCharacters.Count > 0)
            {
                CharId lastChar = VisibleCharacters[^1];
                CharacterVisual visual = world.Terminal.Arena[(int)lastChar.Value].Animation
                    .CurrentCharacterVisual;
                bool lastIsHighlight = visual.Colors?.FgColor is Color fg
                    && fg.Equals(config.HighlightColor);
                if (lastIsHighlight)
                {
                    string symbol = visual.Symbol;
                    Color rainFg = world.Rng.Choice(rainColors);
                    Matrix.SetAppearance(world, lastChar, symbol, ColorPair.New(rainFg, null));
                }

                if (HoldTime != 0)
                {
                    HoldTime -= 1;
                }
                else if (Phase == MatrixColumnPhase.Rain)
                {
                    if (world.Rng.Random() < ColumnDropChance)
                    {
                        DropColumn(world);
                    }

                    TrimColumn(world, rainColors);
                }
            }

            if (VisibleCharacters.Count > Length)
            {
                TrimColumn(world, rainColors);
            }

            ActiveRainFallDelay = BaseRainFallDelay;
        }
        else
        {
            ActiveRainFallDelay -= 1;
        }

        foreach (CharId character in VisibleCharacters)
        {
            string? nextSymbol = world.Rng.Random() < config.SymbolSwapChance
                ? world.Rng.Choice(config.RainSymbols)
                : null;
            Color? nextColor = world.Rng.Random() < config.ColorSwapChance
                ? world.Rng.Choice(rainColors)
                : null;
            if (nextSymbol is null && nextColor is null)
            {
                continue;
            }

            CharacterVisual visual = world.Terminal.Arena[(int)character.Value].Animation
                .CurrentCharacterVisual;
            bool valuesUnchanged =
                (nextSymbol is null || nextSymbol == visual.Symbol)
                && (nextColor is null || (visual.Colors?.FgColor is Color cur && cur.Equals(nextColor)));
            if (valuesUnchanged)
            {
                continue;
            }

            if (nextSymbol is string sym && nextColor is Color col)
            {
                Matrix.SetAppearance(world, character, sym, ColorPair.New(col, null));
            }
            else if (nextSymbol is string symOnly)
            {
                Color? color = visual.Colors?.FgColor;
                Matrix.SetAppearance(world, character, symOnly, ColorPair.New(color, null));
            }
            else if (nextColor is Color colOnly)
            {
                Matrix.SetAppearance(
                    world,
                    character,
                    visual.Symbol,
                    ColorPair.New(colOnly, null));
            }
        }
    }
}

public sealed class Matrix : IEffect
{
    private readonly MatrixConfig _config;
    private readonly List<RainColumn> _columns = new List<RainColumn>();
    private readonly List<int> _pendingColumns = new List<int>();
    private readonly List<int> _activeColumns = new List<int>();
    private readonly List<int> _fullColumns = new List<int>();
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    private List<Color> _rainColors = new List<Color>();
    private long _columnDelay;
    private long _resolveDelay;
    private bool _finalFrameShown;
    private bool _rainComplete;
    private MatrixPhase _phase;
    private double _rainStart;

    public Matrix(MatrixConfig config)
    {
        _config = config;
        _resolveDelay = config.ResolveDelay;
        _finalFrameShown = false;
        _rainComplete = false;
        _phase = MatrixPhase.Rain;
        _rainStart = 0.0;
    }

    public static Matrix FromOptions(Dictionary<string, object> options)
    {
        return new Matrix(new MatrixConfig
        {
            HighlightColor = (Color)options["--highlight-color"],
            RainColorGradient = TypedList<Color>(options, "--rain-color-gradient"),
            RainSymbols = TypedList<string>(options, "--rain-symbols"),
            RainFallDelayRange = ((long, long))options["--rain-fall-delay-range"],
            RainColumnDelayRange = ((long, long))options["--rain-column-delay-range"],
            RainTime = (long)options["--rain-time"],
            SymbolSwapChance = (double)options["--symbol-swap-chance"],
            ColorSwapChance = (double)options["--color-swap-chance"],
            ResolveDelay = (long)options["--resolve-delay"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientFrames = (long)options["--final-gradient-frames"],
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    public static void SetAppearance(EngineWorld world, CharId id, string symbol, ColorPair colors)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        string inputSymbol = ch.InputSymbol;
        bool usesPre = ch.UsesInputPreexistingColors;
        ch.Animation.SetAppearance(inputSymbol, usesPre, symbol, colors);
    }

    private static bool HasInputColors(EngineWorld world, CharId character)
    {
        Animation anim = world.Terminal.Arena[(int)character.Value].Animation;
        return anim.InputFgColor is not null || anim.InputBgColor is not null;
    }

    private MatrixColumnPhase ColumnPhaseFor() => _phase switch
    {
        MatrixPhase.Rain => MatrixColumnPhase.Rain,
        MatrixPhase.Fill => MatrixColumnPhase.Fill,
        _ => throw new EngineInvariantException("columns are never set up during resolve"),
    };

    public void Build(EngineWorld world)
    {
        _rainColors = Gradient.WithSteps(_config.RainColorGradient, 6, false).Spectrum;
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
        foreach (CharId character in characters)
        {
            string inputSymbol;
            Coord inputCoord;
            Color? inputFg;
            Color? inputBg;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)character.Value];
                inputSymbol = ch.InputSymbol;
                inputCoord = ch.InputCoord;
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                usesPre = ch.UsesInputPreexistingColors;
            }

            ColorPair finalColors = dynamic
                ? ColorPair.New(inputFg, inputBg)
                : ColorPair.New(finalGradientMapping.Get(inputCoord), null);
            _characterFinalColorMap[character] = finalColors;
            Color? finalFgColor = finalColors.FgColor;
            Color? finalBgColor = finalColors.BgColor;
            string resolveScn = world.Terminal.Arena[(int)character.Value].Animation
                .NewScene(false, null, null, "resolve", usesPre);
            if (dynamic)
            {
                Gradient? fgGradient = finalFgColor is Color fg
                    ? Gradient.WithSteps([_config.HighlightColor, fg], 8, false)
                    : null;
                Gradient? bgGradient = finalBgColor is Color bg
                    ? Gradient.WithSteps([_config.HighlightColor, bg], 8, false)
                    : null;
                Scene scene = world.Terminal.Arena[(int)character.Value].Animation.Scenes.Get(resolveScn)
                    ?? throw new EngineInvariantException("resolve scene");
                if (fgGradient is not null || bgGradient is not null)
                {
                    scene.ApplyGradientToSymbols(
                        [inputSymbol],
                        _config.FinalGradientFrames,
                        fgGradient,
                        bgGradient);
                }
                else
                {
                    scene.AddFrame(
                        inputSymbol,
                        _config.FinalGradientFrames,
                        new VisualParams { Colors = new ColorPair() });
                }
            }
            else
            {
                Color resolvedFg = finalFgColor
                    ?? throw new EngineInvariantException("non-dynamic final fg color");
                Gradient resolveGradient = Gradient.WithSteps(
                    [_config.HighlightColor, resolvedFg],
                    8,
                    false);
                Scene scene = world.Terminal.Arena[(int)character.Value].Animation.Scenes.Get(resolveScn)
                    ?? throw new EngineInvariantException("resolve scene");
                foreach (Color color in resolveGradient.Spectrum)
                {
                    scene.AddFrame(
                        inputSymbol,
                        _config.FinalGradientFrames,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }
            }
        }

        var allCharsFilter = new CharacterFilter(true, true, true, false);
        List<List<CharId>> grouped = world.Terminal.GetCharactersGrouped(
            allCharsFilter,
            CharacterGroup.ColumnLeftToRight);
        foreach (List<CharId> columnChars in grouped)
        {
            columnChars.Reverse();
            _columns.Add(new RainColumn(world, _config, columnChars));
            _pendingColumns.Add(_columns.Count - 1);
        }

        world.Rng.Shuffle(_pendingColumns);
        _rainStart = world.Clock.NowWall();
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_phase == MatrixPhase.Rain || _phase == MatrixPhase.Fill)
        {
            if (_columnDelay == 0)
            {
                if (_phase == MatrixPhase.Rain)
                {
                    long iterations = world.Rng.Randint(1, 3);
                    for (long i = 0; i < iterations; i++)
                    {
                        if (_pendingColumns.Count > 0)
                        {
                            int idx = _pendingColumns[0];
                            _pendingColumns.RemoveAt(0);
                            _activeColumns.Add(idx);
                        }
                    }
                }
                else
                {
                    while (_pendingColumns.Count > 0)
                    {
                        int idx = _pendingColumns[0];
                        _pendingColumns.RemoveAt(0);
                        _activeColumns.Add(idx);
                    }
                }

                _columnDelay = _phase == MatrixPhase.Rain
                    ? world.Rng.Randint(
                        _config.RainColumnDelayRange.Item1,
                        _config.RainColumnDelayRange.Item2)
                    : 1;
            }
            else
            {
                _columnDelay -= 1;
            }

            var activeSnapshot = new List<int>(_activeColumns);
            foreach (int columnIndex in activeSnapshot)
            {
                _columns[columnIndex].Tick(world, _config, _rainColors);
                RainColumn column = _columns[columnIndex];
                if (column.PendingCharacters.Count == 0)
                {
                    if (column.Phase == MatrixColumnPhase.Fill && !_fullColumns.Contains(columnIndex))
                    {
                        _fullColumns.Add(columnIndex);
                    }
                    else if (column.VisibleCharacters.Count == 0)
                    {
                        column.SetupColumn(world, _config, ColumnPhaseFor());
                        _pendingColumns.Add(columnIndex);
                    }
                }
            }

            _activeColumns.RemoveAll(ci => _columns[ci].VisibleCharacters.Count == 0);
            if (_phase == MatrixPhase.Fill
                && _pendingColumns.Count == 0
                && _activeColumns.TrueForAll(ci =>
                    _columns[ci].PendingCharacters.Count == 0
                    && _columns[ci].Phase == MatrixColumnPhase.Fill))
            {
                _phase = MatrixPhase.Resolve;
                _activeColumns.Clear();
            }

            if (_phase == MatrixPhase.Rain
                && _config.RainTime > 0
                && world.Clock.NowWall() - _rainStart > _config.RainTime)
            {
                _rainComplete = true;
                _phase = MatrixPhase.Fill;
                foreach (int ci in _activeColumns)
                {
                    _columns[ci].HoldTime = 0;
                    _columns[ci].ColumnDropChance = 1.0;
                }

                var pendingSnapshot = new List<int>(_pendingColumns);
                foreach (int ci in pendingSnapshot)
                {
                    _columns[ci].SetupColumn(world, _config, MatrixColumnPhase.Fill);
                }
            }
        }
        else if (_phase == MatrixPhase.Resolve)
        {
            var fullSnapshot = new List<int>(_fullColumns);
            foreach (int columnIndex in fullSnapshot)
            {
                _columns[columnIndex].Tick(world, _config, _rainColors);
                if (_columns[columnIndex].VisibleCharacters.Count > 0)
                {
                    if (_resolveDelay == 0)
                    {
                        long iterations = world.Rng.Randint(1, 4);
                        for (long i = 0; i < iterations; i++)
                        {
                            if (_columns[columnIndex].VisibleCharacters.Count > 0)
                            {
                                CharId nextChar = _columns[columnIndex].ResolveChar(world);
                                string inputSymbol = world.Terminal.Arena[(int)nextChar.Value].InputSymbol;
                                if (inputSymbol != " " || HasInputColors(world, nextChar))
                                {
                                    world.ActivateScene(this, nextChar, "resolve");
                                    world.ActiveCharacters.Insert(
                                        nextChar,
                                        world.Terminal.Arena[(int)nextChar.Value].CharacterId);
                                }
                                else
                                {
                                    world.Terminal.SetCharacterVisibility(nextChar, false);
                                }
                            }
                        }

                        _resolveDelay = _config.ResolveDelay;
                    }
                    else
                    {
                        _resolveDelay -= 1;
                    }
                }
            }

            _fullColumns.RemoveAll(ci => _columns[ci].VisibleCharacters.Count == 0);
        }

        if (_fullColumns.Count > 0
            || _activeColumns.Count > 0
            || !world.ActiveCharacters.IsEmpty
            || _pendingColumns.Count > 0
            || !_rainComplete)
        {
            world.Update(this);
            return world.Frame();
        }

        if (!_finalFrameShown)
        {
            _finalFrameShown = true;
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
