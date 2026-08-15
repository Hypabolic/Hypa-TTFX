using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>overflow, ported from effects/effect_overflow.py. Transcribed from <c>effects/overflow.rs</c>.</summary>
public sealed class OverflowConfig
{
    public List<Color> OverflowGradientStops { get; set; } = new List<Color>();
    public (long Lower, long Upper) OverflowCyclesRange { get; set; } = (2, 4);
    public long OverflowSpeed { get; set; } = 3;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

/// <summary>OverflowIterator.Row.</summary>
internal sealed class OverflowRow
{
    public List<CharId> Characters { get; }
    public bool Final { get; }

    public OverflowRow(List<CharId> characters, bool final)
    {
        Characters = characters;
        Final = final;
    }

    /// <summary>Row.move_up.</summary>
    public void MoveUp(EngineWorld world)
    {
        foreach (CharId id in Characters)
        {
            Motion motion = world.Terminal.Arena[(int)id.Value].Motion;
            Coord current = motion.CurrentCoord;
            motion.SetCoordinate(Coord.New(current.Column, current.Row + 1));
        }
    }

    /// <summary>Row.setup.</summary>
    public void Setup(EngineWorld world)
    {
        foreach (CharId id in Characters)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            long column = ch.InputCoord.Column;
            ch.Motion.SetCoordinate(Coord.New(column, 0));
        }
    }

    /// <summary>Row.set_color.</summary>
    public void SetColor(EngineWorld world, Color? fgColor, Color? bgColor)
    {
        foreach (CharId id in Characters)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string inputSymbol = ch.InputSymbol;
            bool usesPre = ch.UsesInputPreexistingColors;
            ch.Animation.SetAppearance(
                inputSymbol,
                usesPre,
                inputSymbol,
                ColorPair.New(fgColor, bgColor));
        }
    }
}

public sealed class Overflow : IEffect
{
    private readonly OverflowConfig _config;
    // overflow.rs:95 — VecDeque push_back / pop_front
    private readonly Ttfx.Utils.Queue<OverflowRow> _pendingRows;
    private readonly List<OverflowRow> _activeRows;
    private readonly Dictionary<CharId, Color> _characterFinalColorMap;
    private long _delay;
    private Gradient? _overflowGradient;

    public Overflow(OverflowConfig config)
    {
        _config = config;
        _pendingRows = new Ttfx.Utils.Queue<OverflowRow>();
        _activeRows = new List<OverflowRow>();
        _characterFinalColorMap = new Dictionary<CharId, Color>();
        _delay = 0;
        _overflowGradient = null;
    }

    public static Overflow FromOptions(Dictionary<string, object> options)
    {
        (long lower, long upper) cycles = ((long, long))options["--overflow-cycles-range"];
        return new Overflow(new OverflowConfig
        {
            OverflowGradientStops = TypedList<Color>(options, "--overflow-gradient-stops"),
            OverflowCyclesRange = (cycles.Item1, cycles.Item2),
            OverflowSpeed = (long)options["--overflow-speed"],
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

        CharacterFilter fillsFilter = new CharacterFilter(true, true, true, false);
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            fillsFilter,
            CharacterSort.TopToBottomLeftToRight);

        foreach (CharId id in characters)
        {
            Coord coord = world.Terminal.Arena[(int)id.Value].InputCoord;
            Color color = finalGradientMapping.Get(coord) ?? Color.FromHex("000000");
            _characterFinalColorMap[id] = color;
        }

        (long lowerRange, long upperRange) = _config.OverflowCyclesRange;
        List<List<CharId>> rows = world.Terminal.GetCharactersGrouped(
            CharacterFilter.Default,
            CharacterGroup.RowTopToBottom);

        if (upperRange > 0)
        {
            long cycleCount = world.Rng.Randint(lowerRange, upperRange);
            for (long cycle = 0; cycle < cycleCount; cycle++)
            {
                world.Rng.Shuffle(rows);
                foreach (List<CharId> row in rows)
                {
                    var copiedCharacters = new List<CharId>();
                    foreach (CharId id in row)
                    {
                        string symbol;
                        Coord coord;
                        string? fgSeq;
                        string? bgSeq;
                        bool noColor;
                        bool useXterm;
                        Color? inputFg;
                        Color? inputBg;
                        {
                            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                            symbol = ch.InputSymbol;
                            coord = ch.InputCoord;
                            fgSeq = ch.InputAnsiFgSequence;
                            bgSeq = ch.InputAnsiBgSequence;
                            noColor = ch.Animation.NoColor;
                            useXterm = ch.Animation.UseXtermColors;
                            inputFg = ch.Animation.InputFgColor;
                            inputBg = ch.Animation.InputBgColor;
                        }

                        CharId copyId = world.Terminal.AddCharacter(symbol, coord);
                        EffectCharacter copy = world.Terminal.Arena[(int)copyId.Value];
                        copy.Animation.ExistingColorHandling = world.Terminal.Config.ExistingColorHandling;
                        copy.UsesInputPreexistingColors = true;
                        copy.InputAnsiFgSequence = fgSeq;
                        copy.InputAnsiBgSequence = bgSeq;
                        copy.Animation.NoColor = noColor;
                        copy.Animation.UseXtermColors = useXterm;
                        copy.Animation.InputFgColor = inputFg;
                        copy.Animation.InputBgColor = inputBg;
                        copiedCharacters.Add(copyId);
                    }

                    _pendingRows.PushBack(new OverflowRow(copiedCharacters, false));
                }
            }
        }

        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        foreach (List<CharId> row in world.Terminal.GetCharactersGrouped(
                     fillsFilter,
                     CharacterGroup.RowTopToBottom))
        {
            foreach (CharId id in row)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string currentSymbol = ch.Animation.CurrentCharacterVisual.Symbol;
                string inputSymbol = ch.InputSymbol;
                bool usesPre = ch.UsesInputPreexistingColors;
                if (dynamic)
                {
                    Color? inputFg = ch.Animation.InputFgColor;
                    Color? inputBg = ch.Animation.InputBgColor;
                    if (inputFg is not null || inputBg is not null)
                    {
                        ch.Animation.SetAppearance(
                            inputSymbol,
                            usesPre,
                            currentSymbol,
                            ColorPair.New(inputFg, inputBg));
                    }
                    else
                    {
                        ch.Animation.SetAppearance(
                            inputSymbol,
                            usesPre,
                            currentSymbol,
                            new ColorPair());
                    }
                }
                else
                {
                    Color finalColor = _characterFinalColorMap[id];
                    ch.Animation.SetAppearance(
                        inputSymbol,
                        usesPre,
                        currentSymbol,
                        ColorPair.New(finalColor, null));
                }
            }

            _pendingRows.PushBack(new OverflowRow(row, true));
        }

        _delay = 0;
        long steps = Math.Max(
            PyCompat.FloorDiv(
                world.Terminal.Canvas.Top,
                Math.Max(1, _config.OverflowGradientStops.Count - 1)),
            1);
        _overflowGradient = Gradient.WithSteps(_config.OverflowGradientStops, steps, false);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!_pendingRows.IsEmpty)
        {
            if (_delay == 0)
            {
                IReadOnlyList<Color> spectrum = _overflowGradient!.Spectrum;
                long iterations = world.Rng.Randint(1, _config.OverflowSpeed);
                for (long i = 0; i < iterations; i++)
                {
                    if (!_pendingRows.IsEmpty)
                    {
                        foreach (OverflowRow row in _activeRows)
                        {
                            row.MoveUp(world);
                            if (!row.Final)
                            {
                                long headRow = world.Terminal.Arena[(int)row.Characters[0].Value].Motion.CurrentCoord.Row;
                                int index = (int)Math.Min(headRow, spectrum.Count - 1);
                                row.SetColor(world, spectrum[index], null);
                            }
                        }

                        OverflowRow nextRow = _pendingRows.PopFront();
                        nextRow.Setup(world);
                        nextRow.MoveUp(world);
                        if (!nextRow.Final)
                        {
                            nextRow.SetColor(world, spectrum[0], null);
                        }

                        foreach (CharId id in nextRow.Characters)
                        {
                            world.Terminal.SetCharacterVisibility(id, true);
                        }

                        _activeRows.Add(nextRow);
                    }
                }

                _delay = world.Rng.Randint(0, 3);
            }
            else
            {
                _delay -= 1;
            }

            long canvasTop = world.Terminal.Canvas.Top;
            _activeRows.RemoveAll(row =>
                world.Terminal.Arena[(int)row.Characters[0].Value].Motion.CurrentCoord.Row > canvasTop);

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
