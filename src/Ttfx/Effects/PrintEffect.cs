using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>print, ported from effects/effect_print.py. Transcribed from <c>effects/print_effect.rs</c>.</summary>
public sealed class PrintEffectConfig
{
    public double PrintHeadReturnSpeed { get; set; } = 1.5;
    public long PrintSpeed { get; set; } = 2;
    public Easing PrintHeadEasing { get; set; } = Easing.InOutQuad;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
}

/// <summary>PrintIterator.Row.</summary>
internal sealed class PrintRow
{
    public List<CharId> UntypedChars { get; }
    public List<CharId> TypedChars { get; }

    public PrintRow(List<CharId> untypedChars, List<CharId> typedChars)
    {
        UntypedChars = untypedChars;
        TypedChars = typedChars;
    }

    /// <summary>Row.move_up.</summary>
    public void MoveUp(EngineWorld world)
    {
        foreach (CharId id in TypedChars)
        {
            Motion motion = world.Terminal.Arena[(int)id.Value].Motion;
            Coord current = motion.CurrentCoord;
            motion.SetCoordinate(Coord.New(current.Column, current.Row + 1));
        }
    }

    /// <summary>Row.type_char.</summary>
    public CharId? TypeChar()
    {
        if (UntypedChars.Count == 0)
        {
            return null;
        }

        // print_effect.rs:73 — untyped_chars.remove(0)
        CharId nextChar = UntypedChars[0];
        UntypedChars.RemoveAt(0);
        TypedChars.Add(nextChar);
        return nextChar;
    }
}

public sealed class PrintEffect : IEffect
{
    private const uint SetInvisibleCallback = 0;

    private readonly PrintEffectConfig _config;
    private readonly List<PrintRow> _pendingRows;
    private readonly List<PrintRow> _processedRows;
    private CharId _typingHead;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private PrintRow _currentRow;
    private bool _typing;
    private long _lastColumn;

    public PrintEffect(PrintEffectConfig config)
    {
        _config = config;
        _pendingRows = new List<PrintRow>();
        _processedRows = new List<PrintRow>();
        _typingHead = new CharId(0);
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _currentRow = new PrintRow(new List<CharId>(), new List<CharId>());
        _typing = false;
        _lastColumn = 0;
    }

    public static PrintEffect FromOptions(Dictionary<string, object> options)
    {
        return new PrintEffect(new PrintEffectConfig
        {
            PrintHeadReturnSpeed = (double)options["--print-head-return-speed"],
            PrintSpeed = (long)options["--print-speed"],
            PrintHeadEasing = (Easing)options["--print-head-easing"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
        if (callback.Id == SetInvisibleCallback)
        {
            world.Terminal.SetCharacterVisibility(character, false);
        }
    }

    /// <summary>PrintIterator.Row.__init__ via make_row.</summary>
    private PrintRow MakeRow(EngineWorld world, List<CharId> characters)
    {
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        Color typingHeadColor = Color.FromHex("FFFFFF");
        bool allSpaces = true;
        foreach (CharId id in characters)
        {
            if (world.Terminal.Arena[(int)id.Value].InputSymbol != " ")
            {
                allSpaces = false;
                break;
            }
        }

        List<CharId> rowCharacters;
        if (allSpaces)
        {
            rowCharacters = characters.Count > 0 ? [characters[0]] : new List<CharId>();
        }
        else
        {
            long? rightExtent = null;
            foreach (CharId id in characters)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                if (!ch.IsFillCharacter)
                {
                    long column = ch.InputCoord.Column;
                    if (rightExtent is null || column > rightExtent)
                    {
                        rightExtent = column;
                    }
                }
            }

            if (rightExtent is null)
            {
                throw new EngineInvariantException("row has a non-fill character");
            }

            rowCharacters = new List<CharId>();
            foreach (CharId id in characters)
            {
                if (world.Terminal.Arena[(int)id.Value].InputCoord.Column <= rightExtent)
                {
                    rowCharacters.Add(id);
                }
            }
        }

        var untypedChars = new List<CharId>();
        foreach (CharId id in rowCharacters)
        {
            string inputSymbol;
            long inputColumn;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputSymbol = ch.InputSymbol;
                inputColumn = ch.InputCoord.Column;
                usesPre = ch.UsesInputPreexistingColors;
            }

            string typedAnimation;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(Coord.New(inputColumn, 1));
                typedAnimation = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            if (dynamic)
            {
                ColorPair finalColors = _characterFinalColorMap[id];
                Gradient? fgGradient = finalColors.FgColor is not null
                    ? Gradient.WithSteps([typingHeadColor, finalColors.FgColor], 5, false)
                    : null;
                Gradient? bgGradient = finalColors.BgColor is not null
                    ? Gradient.WithSteps([typingHeadColor, finalColors.BgColor], 5, false)
                    : null;
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(typedAnimation)
                    ?? throw new EngineInvariantException("typed animation scene");
                if (fgGradient is not null || bgGradient is not null)
                {
                    scene.ApplyGradientToSymbols(
                        ["█", "▓", "▒", "░", inputSymbol],
                        3,
                        fgGradient,
                        bgGradient);
                }
                else
                {
                    Gradient headGradient = Gradient.WithSteps([typingHeadColor, typingHeadColor], 4, false);
                    scene.ApplyGradientToSymbols(
                        ["█", "▓", "▒", "░"],
                        3,
                        headGradient,
                        null);
                    scene.AddFrame(
                        inputSymbol,
                        3,
                        new VisualParams { Colors = new ColorPair() });
                }
            }
            else
            {
                Color finalFg = _characterFinalColorMap[id].FgColor
                    ?? throw new EngineInvariantException("final fg color present");
                Gradient colorGradient = Gradient.WithSteps([typingHeadColor, finalFg], 5, false);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.Scenes.Get(typedAnimation)!
                    .ApplyGradientToSymbols(
                        ["█", "▓", "▒", "░", inputSymbol],
                        3,
                        colorGradient,
                        null);
            }

            world.ActivateScene(this, id, typedAnimation);
            untypedChars.Add(id);
        }

        return new PrintRow(untypedChars, new List<CharId>());
    }

    public void Build(EngineWorld world)
    {
        _typingHead = world.Terminal.AddCharacter("█", Coord.New(1, 1));

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
        CharacterFilter filter = new CharacterFilter(true, true, true, false);
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            filter,
            CharacterSort.TopToBottomLeftToRight);

        foreach (CharId id in characters)
        {
            ColorPair finalColors;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                if (dynamic)
                {
                    finalColors = ColorPair.New(ch.Animation.InputFgColor, ch.Animation.InputBgColor);
                }
                else
                {
                    finalColors = ColorPair.New(
                        finalGradientMapping.Get(ch.InputCoord) ?? Color.FromHex("FFFFFF"),
                        null);
                }
            }

            _characterFinalColorMap[id] = finalColors;
        }

        foreach (List<CharId> inputRow in world.Terminal.GetCharactersGrouped(
                     filter,
                     CharacterGroup.RowTopToBottom))
        {
            _pendingRows.Add(MakeRow(world, inputRow));
        }

        // print_effect.rs:288 — pending_rows.remove(0)
        _currentRow = _pendingRows[0];
        _pendingRows.RemoveAt(0);
        _typing = true;
        _lastColumn = 0;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!world.ActiveCharacters.IsEmpty || _typing)
        {
            if (world.Terminal.Arena[(int)_typingHead.Value].Motion.ActivePath is not null)
            {
                // print head is performing a carriage return
            }
            else if (_currentRow.UntypedChars.Count > 0)
            {
                long count = Math.Min(_currentRow.UntypedChars.Count, _config.PrintSpeed);
                for (long i = 0; i < count; i++)
                {
                    CharId? nextChar = _currentRow.TypeChar();
                    if (nextChar is CharId typed)
                    {
                        world.Terminal.SetCharacterVisibility(typed, true);
                        world.ActiveCharacters.Insert(
                            typed,
                            world.Terminal.Arena[(int)typed.Value].CharacterId);
                        _lastColumn = world.Terminal.Arena[(int)typed.Value].InputCoord.Column;
                    }
                }
            }
            else
            {
                PrintRow finishedRow = _currentRow;
                _currentRow = new PrintRow(new List<CharId>(), new List<CharId>());
                _processedRows.Add(finishedRow);
                if (_pendingRows.Count > 0)
                {
                    foreach (PrintRow row in _processedRows)
                    {
                        row.MoveUp(world);
                    }

                    // print_effect.rs:318 — pending_rows.remove(0)
                    _currentRow = _pendingRows[0];
                    _pendingRows.RemoveAt(0);

                    bool lastRowAllFill = true;
                    foreach (CharId id in _processedRows[^1].TypedChars)
                    {
                        if (!world.Terminal.Arena[(int)id.Value].IsFillCharacter)
                        {
                            lastRowAllFill = false;
                            break;
                        }
                    }

                    bool currentRowAllFill = true;
                    foreach (CharId id in _currentRow.UntypedChars)
                    {
                        if (!world.Terminal.Arena[(int)id.Value].IsFillCharacter)
                        {
                            currentRowAllFill = false;
                            break;
                        }
                    }

                    if (!lastRowAllFill && !currentRowAllFill)
                    {
                        long? leftExtent = null;
                        foreach (CharId id in _currentRow.UntypedChars)
                        {
                            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                            if (!ch.IsFillCharacter)
                            {
                                long column = ch.InputCoord.Column;
                                if (leftExtent is null || column < leftExtent)
                                {
                                    leftExtent = column;
                                }
                            }
                        }

                        if (leftExtent is null)
                        {
                            throw new EngineInvariantException("row has a non-fill character");
                        }

                        long textRight = world.Terminal.Canvas.TextRight;
                        _currentRow.UntypedChars.RemoveAll(id =>
                        {
                            long column = world.Terminal.Arena[(int)id.Value].InputCoord.Column;
                            return column < leftExtent || column > textRight;
                        });
                    }

                    {
                        EffectCharacter head = world.Terminal.Arena[(int)_typingHead.Value];
                        head.Motion.SetCoordinate(Coord.New(_lastColumn, 1));
                    }

                    world.Terminal.SetCharacterVisibility(_typingHead, true);
                    long targetColumn = world.Terminal.Arena[(int)_currentRow.UntypedChars[0].Value].InputCoord.Column;
                    {
                        EffectCharacter head = world.Terminal.Arena[(int)_typingHead.Value];
                        head.Motion.Paths.Clear();
                        string pathId = head.Motion.NewPath(
                            _config.PrintHeadReturnSpeed,
                            _config.PrintHeadEasing,
                            null,
                            0,
                            false,
                            "carriage_return_path");
                        head.Motion.Paths.Get(pathId)!
                            .NewWaypoint(Coord.New(targetColumn, 1), null, "");
                    }

                    world.ActivatePath(this, _typingHead, "carriage_return_path");
                    TryRegisterInvisibleCallback(world);
                    world.ActiveCharacters.Insert(
                        _typingHead,
                        world.Terminal.Arena[(int)_typingHead.Value].CharacterId);
                }
                else
                {
                    _typing = false;
                }
            }

            world.Update(this);
            return world.Frame();
        }

        return null;
    }

    private void TryRegisterInvisibleCallback(EngineWorld world)
    {
        try
        {
            world.RegisterEvent(
                _typingHead,
                Event.PathComplete,
                new CallerKey.Path("carriage_return_path"),
                new EventAction.Callback(new EffectCallback(SetInvisibleCallback, [])));
        }
        catch (EngineException ex) when (ex.Message.Contains("duplicate event registration", StringComparison.Ordinal))
        {
        }
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
