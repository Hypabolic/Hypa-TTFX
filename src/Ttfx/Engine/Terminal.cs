using System;
using System.Buffers;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Terminal: config, canvas assembly, character queries, renderer.
/// Transcribed from <c>engine/terminal.rs</c>.
/// </summary>
public sealed class TerminalConfig
{
    public long TabWidth { get; set; } = 4;
    public bool XtermColors { get; set; }
    public bool NoColor { get; set; }
    public Color TerminalBackgroundColor { get; set; } = Color.FromHex("000000");
    public ExistingColorHandling ExistingColorHandling { get; set; } = ExistingColorHandling.Ignore;
    public bool WrapText { get; set; }
    public long FrameRate { get; set; } = 60;
    public long CanvasWidth { get; set; } = -1;
    public long CanvasHeight { get; set; } = -1;
    public Anchor AnchorCanvas { get; set; } = Anchor.Sw;
    public Anchor AnchorText { get; set; } = Anchor.Sw;
    public bool IgnoreTerminalDimensions { get; set; }
    public bool ReuseCanvas { get; set; }
    public bool NoEol { get; set; }
    public bool NoRestoreCursor { get; set; }

    public static TerminalConfig FromRoot(RootOptions root)
    {
        return new TerminalConfig
        {
            TabWidth = root.TabWidth,
            XtermColors = root.XtermColors,
            NoColor = root.NoColor,
            TerminalBackgroundColor = root.TerminalBackgroundColor,
            ExistingColorHandling = root.ExistingColorHandling,
            WrapText = root.WrapText,
            FrameRate = root.FrameRate,
            CanvasWidth = root.CanvasWidth,
            CanvasHeight = root.CanvasHeight,
            AnchorCanvas = root.AnchorCanvas,
            AnchorText = root.AnchorText,
            IgnoreTerminalDimensions = root.IgnoreTerminalDimensions,
            ReuseCanvas = root.ReuseCanvas,
            NoEol = root.NoEol,
            NoRestoreCursor = root.NoRestoreCursor,
        };
    }
}

/// <summary>
/// Everything about the drawing area that is derived from the terminal size.
/// A resize only matters if recomputing this yields something different, so it
/// is factored out of Terminal::new rather than inlined there.
/// </summary>
public readonly record struct Layout(
    long CanvasHeight,
    long CanvasWidth,
    long ColumnOffset,
    long RowOffset,
    long VisibleTop,
    long VisibleBottom,
    long VisibleRight,
    long VisibleLeft);

public sealed class Terminal
{
    private const uint EmptyRenderCell = uint.MaxValue;
    private const int NotVisible = int.MaxValue;

    public TerminalConfig Config { get; }
    public Canvas Canvas { get; }
    public List<EffectCharacter> Arena { get; }
    public uint NextCharacterId { get; private set; }
    public ColorFrequency InputColorsFrequency { get; }
    public (long Width, long Height) TerminalDimensions { get; }
    public Layout Layout { get; }
    public long CanvasColumnOffset { get; }
    public long CanvasRowOffset { get; }
    public long VisibleTop { get; }
    public long VisibleBottom { get; }
    public long VisibleRight { get; }
    public long VisibleLeft { get; }
    public List<CharId> InputCharacters { get; }
    public List<CharId> AddedCharacters { get; } = new List<CharId>();
    public Dictionary<Coord, CharId> CharacterByInputCoord { get; }
    public List<CharId> InnerFillCharacters { get; } = new List<CharId>();
    public List<CharId> OuterFillCharacters { get; } = new List<CharId>();

    private readonly List<CharId> _visibleCharacters = new List<CharId>();
    private readonly List<int> _visiblePositions;
    private uint[] _renderCells = [];
    private readonly ArrayBufferWriter<byte> _outputBuffer = new ArrayBufferWriter<byte>();

    private Terminal(
        TerminalConfig config,
        Canvas canvas,
        List<EffectCharacter> arena,
        uint nextCharacterId,
        ColorFrequency inputColorsFrequency,
        (long, long) terminalDimensions,
        Layout layout,
        List<CharId> inputCharacters,
        Dictionary<Coord, CharId> characterByInputCoord)
    {
        Config = config;
        Canvas = canvas;
        Arena = arena;
        NextCharacterId = nextCharacterId;
        InputColorsFrequency = inputColorsFrequency;
        TerminalDimensions = terminalDimensions;
        Layout = layout;
        CanvasColumnOffset = layout.ColumnOffset;
        CanvasRowOffset = layout.RowOffset;
        VisibleTop = layout.VisibleTop;
        VisibleBottom = layout.VisibleBottom;
        VisibleRight = layout.VisibleRight;
        VisibleLeft = layout.VisibleLeft;
        InputCharacters = inputCharacters;
        CharacterByInputCoord = characterByInputCoord;
        _visiblePositions = new List<int>(arena.Count);
        for (int i = 0; i < arena.Count; i++)
        {
            _visiblePositions.Add(NotVisible);
        }
    }

    public static Terminal New(string inputData, TerminalConfig config)
    {
        if (inputData.Length == 0)
        {
            inputData = "No Input.";
        }

        var arena = new List<EffectCharacter>();
        uint nextCharacterId = 0;
        var inputColorsFrequency = new ColorFrequency();

        var preprocessor = new Preprocessor(arena, nextCharacterId, inputColorsFrequency, config);
        List<List<CharId>> preprocessedLines = preprocessor.Preprocess(inputData);
        nextCharacterId = preprocessor.NextCharacterId;

        var inputLineLengths = new List<long>(preprocessedLines.Count);
        foreach (List<CharId> line in preprocessedLines)
        {
            inputLineLengths.Add(line.Count);
        }

        (long termWidth, long termHeight) = GetTerminalDimensions();
        Layout layout = ComputeLayout(config, inputLineLengths, termWidth, termHeight);
        var canvas = Canvas.New(layout.CanvasHeight, layout.CanvasWidth);

        List<CharId> setupIds = SetupInputCharacters(config, canvas, arena, preprocessedLines);
        var inputCharacters = new List<CharId>();
        foreach (CharId id in setupIds)
        {
            Coord coord = arena[(int)id.Value].InputCoord;
            if (coord.Row <= canvas.Top && coord.Column <= canvas.Right)
            {
                inputCharacters.Add(id);
            }
        }

        var characterByInputCoord = new Dictionary<Coord, CharId>();
        foreach (CharId id in inputCharacters)
        {
            characterByInputCoord[arena[(int)id.Value].InputCoord] = id;
        }

        var terminal = new Terminal(
            config,
            canvas,
            arena,
            nextCharacterId,
            inputColorsFrequency,
            (termWidth, termHeight),
            layout,
            inputCharacters,
            characterByInputCoord);
        terminal.MakeFillCharacters();
        terminal.SetupCharacterNeighbors();
        return terminal;
    }

    /// <summary>
    /// EngineCtx preexisting_colors_present scan (ctx.rs:108-111): any surviving
    /// input character carries an input fg or bg captured at parse time.
    /// </summary>
    public bool PreexistingColorsPresent()
    {
        foreach (CharId id in InputCharacters)
        {
            EffectCharacter ch = Arena[(int)id.Value];
            if (ch.Animation.InputFgColor is not null || ch.Animation.InputBgColor is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Terminal._make_fill_characters: row-major from (1,1), fresh space chars
    /// for unoccupied canvas coords, split inner/outer by the text bounds.
    /// </summary>
    private void MakeFillCharacters()
    {
        for (long row = 1; row <= Canvas.Top; row++)
        {
            for (long column = 1; column <= Canvas.Right; column++)
            {
                var coord = Coord.New(column, row);
                if (!CharacterByInputCoord.ContainsKey(coord))
                {
                    var fill = new EffectCharacter(NextCharacterId, " ", column, row);
                    fill.IsFillCharacter = true;
                    fill.Animation.NoColor = Config.NoColor;
                    fill.Animation.UseXtermColors = Config.XtermColors;
                    fill.Animation.ExistingColorHandling = Config.ExistingColorHandling;
                    fill.UsesInputPreexistingColors = false;
                    NextCharacterId += 1;
                    var id = new CharId((uint)Arena.Count);
                    Arena.Add(fill);
                    CharacterByInputCoord[coord] = id;
                    if (Canvas.TextLeft <= column
                        && column <= Canvas.TextRight
                        && Canvas.TextBottom <= row
                        && row <= Canvas.TextTop)
                    {
                        InnerFillCharacters.Add(id);
                    }
                    else
                    {
                        OuterFillCharacters.Add(id);
                    }
                }
            }
        }
    }

    private void SetupCharacterNeighbors()
    {
        var coords = new List<(Coord Coord, CharId Id)>(CharacterByInputCoord.Count);
        foreach (KeyValuePair<Coord, CharId> pair in CharacterByInputCoord)
        {
            coords.Add((pair.Key, pair.Value));
        }

        foreach ((Coord coord, CharId id) in coords)
        {
            CharId? n = CharacterByInputCoord.TryGetValue(Coord.New(coord.Column, coord.Row + 1), out CharId nv) ? nv : null;
            CharId? e = CharacterByInputCoord.TryGetValue(Coord.New(coord.Column + 1, coord.Row), out CharId ev) ? ev : null;
            CharId? s = CharacterByInputCoord.TryGetValue(Coord.New(coord.Column, coord.Row - 1), out CharId sv) ? sv : null;
            CharId? w = CharacterByInputCoord.TryGetValue(Coord.New(coord.Column - 1, coord.Row), out CharId wv) ? wv : null;
            EffectCharacter ch = Arena[(int)id.Value];
            var neighbors = ch.Neighbors;
            neighbors.North = n;
            neighbors.East = e;
            neighbors.South = s;
            neighbors.West = w;
            ch.Neighbors = neighbors;
        }
    }

    public void SetCharacterVisibility(CharId id, bool isVisible)
    {
        int arenaIndex = (int)id.Value;
        if (Arena[arenaIndex].IsVisible == isVisible)
        {
            return;
        }

        Arena[arenaIndex].IsVisible = isVisible;
        while (_visiblePositions.Count < Arena.Count)
        {
            _visiblePositions.Add(NotVisible);
        }

        if (isVisible)
        {
            _visiblePositions[arenaIndex] = _visibleCharacters.Count;
            _visibleCharacters.Add(id);
        }
        else
        {
            int position = _visiblePositions[arenaIndex];
            _visiblePositions[arenaIndex] = NotVisible;
            // swap_remove: swap-with-last, not a shifting RemoveAt (terminal.rs:336).
            int last = _visibleCharacters.Count - 1;
            _visibleCharacters[position] = _visibleCharacters[last];
            _visibleCharacters.RemoveAt(last);
            if (position < _visibleCharacters.Count)
            {
                CharId moved = _visibleCharacters[position];
                _visiblePositions[(int)moved.Value] = position;
            }
        }
    }

    /// <summary>
    /// Paint the visible characters into the reusable cell buffer using the
    /// canonical (layer, character_id) painter order (plan.md §4.3).
    /// </summary>
    private (int Width, int Height) UpdateRenderCells()
    {
        int width = (int)Math.Max(VisibleRight, 0);
        int height = (int)Math.Max(VisibleTop, 0);
        int cellCount;
        try
        {
            cellCount = checked(width * height);
        }
        catch (OverflowException)
        {
            throw new EngineInvariantException("terminal canvas is too large");
        }

        if (_renderCells.Length != cellCount)
        {
            _renderCells = new uint[cellCount];
        }

        Array.Fill(_renderCells, EmptyRenderCell);

        // The old implementation sorted every visible character by painter
        // order and overwrote cells in that order.  A cell only needs the
        // maximum key, so select that winner directly and avoid the per-frame
        // allocation and O(n log n) sort.
        foreach (CharId id in _visibleCharacters)
        {
            EffectCharacter ch = Arena[(int)id.Value];
            long row = ch.Motion.CurrentCoord.Row + CanvasRowOffset;
            long column = ch.Motion.CurrentCoord.Column + CanvasColumnOffset;
            if (VisibleBottom <= row
                && row <= VisibleTop
                && VisibleLeft <= column
                && column <= VisibleRight)
            {
                int cellIndex = (int)(row - 1) * width + (int)(column - 1);
                uint cell = _renderCells[cellIndex];
                if (cell == EmptyRenderCell)
                {
                    _renderCells[cellIndex] = id.Value;
                }
                else
                {
                    EffectCharacter painted = Arena[(int)cell];
                    if ((ch.Layer, ch.CharacterId).CompareTo((painted.Layer, painted.CharacterId)) > 0)
                    {
                        _renderCells[cellIndex] = id.Value;
                    }
                }
            }
        }

        return (width, height);
    }

    /// <summary>get_formatted_output_string: refresh + emit top row first.</summary>
    public ReadOnlyMemory<byte> GetFormattedOutputString()
    {
        (int width, int height) = UpdateRenderCells();
        int minimumCapacity;
        try
        {
            minimumCapacity = checked(width * height + Math.Max(height - 1, 0));
        }
        catch (OverflowException)
        {
            throw new EngineInvariantException("terminal canvas is too large");
        }

        _outputBuffer.ResetWrittenCount();
        if (_outputBuffer.FreeCapacity < minimumCapacity)
        {
            // Grow the recycled buffer like Rust out.reserve(minimum_capacity).
            // GetSpan only ensures capacity; WrittenCount stays 0.
            _ = _outputBuffer.GetSpan(minimumCapacity);
        }

        for (int rowIndex = height - 1; rowIndex >= 0; rowIndex--)
        {
            if (rowIndex + 1 < height)
            {
                _outputBuffer.Write("\n"u8);
            }

            int rowStart = rowIndex * width;
            for (int col = 0; col < width; col++)
            {
                uint cell = _renderCells[rowStart + col];
                if (cell == EmptyRenderCell)
                {
                    _outputBuffer.Write(" "u8);
                }
                else
                {
                    Arena[(int)cell].Animation.CurrentCharacterVisual.FormattedSymbol.AppendTo(_outputBuffer);
                }
            }
        }

        return _outputBuffer.WrittenMemory;
    }

    /// <summary>
    /// shutil.get_terminal_size semantics: COLUMNS/LINES env vars win if both
    /// parse as i64 (Rust grammar — no surrounding whitespace); else query the
    /// tty; on failure (80, 24). Per-axis override when only one env var parses.
    /// </summary>
    public static (long Width, long Height) GetTerminalDimensions()
    {
        long? columns = EnvDim("COLUMNS");
        long? lines = EnvDim("LINES");
        if (columns is long c && lines is long l)
        {
            return (c, l);
        }

        (long Width, long Height)? tty = PosixTerminal.QueryTtySize();
        if (tty is { } size)
        {
            return (columns ?? size.Width, lines ?? size.Height);
        }

        return (columns ?? 80, lines ?? 24);
    }

    private static long? EnvDim(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (value is null)
        {
            return null;
        }

        // Rust parse::<i64>() rejects surrounding whitespace.
        if (ValueParsers.TryParseI64(value, out long parsed))
        {
            return parsed;
        }

        return null;
    }

    internal static Layout ComputeLayout(
        TerminalConfig config,
        IReadOnlyList<long> lineLengths,
        long terminalWidth,
        long terminalHeight)
    {
        (long canvasHeight, long canvasWidth) =
            GetCanvasDimensions(config, lineLengths, terminalWidth, terminalHeight);
        var canvas = Canvas.New(canvasHeight, canvasWidth);
        long width = terminalWidth;
        long height = terminalHeight;
        long columnOffset;
        long rowOffset;
        if (!config.IgnoreTerminalDimensions)
        {
            (columnOffset, rowOffset) = CalcCanvasOffsets(config, canvas, width, height);
        }
        else
        {
            width = canvas.Right;
            height = canvas.Top;
            columnOffset = 0;
            rowOffset = 0;
        }

        return new Layout(
            canvasHeight,
            canvasWidth,
            columnOffset,
            rowOffset,
            Math.Min(canvas.Top + rowOffset, height),
            Math.Max(canvas.Bottom + rowOffset, 1),
            Math.Min(canvas.Right + columnOffset, width),
            Math.Max(canvas.Left + columnOffset, 1));
    }

    /// <summary>Terminal._get_canvas_dimensions -&gt; (height, width).</summary>
    internal static (long Height, long Width) GetCanvasDimensions(
        TerminalConfig config,
        IReadOnlyList<long> lineLengths,
        long terminalWidth,
        long terminalHeight)
    {
        long canvasWidth;
        if (config.CanvasWidth > 0)
        {
            canvasWidth = config.CanvasWidth;
        }
        else if (config.CanvasWidth == 0)
        {
            canvasWidth = terminalWidth;
        }
        else
        {
            long inputWidth = 0;
            foreach (long length in lineLengths)
            {
                if (length > inputWidth)
                {
                    inputWidth = length;
                }
            }

            canvasWidth = config.IgnoreTerminalDimensions
                ? inputWidth
                : Math.Min(terminalWidth, inputWidth);
        }

        long canvasHeight;
        if (config.CanvasHeight > 0)
        {
            canvasHeight = config.CanvasHeight;
        }
        else if (config.CanvasHeight == 0)
        {
            canvasHeight = terminalHeight;
        }
        else
        {
            long inputHeight = lineLengths.Count;
            if (config.IgnoreTerminalDimensions)
            {
                canvasHeight = inputHeight;
            }
            else if (config.WrapText)
            {
                canvasHeight = Math.Min(WrappedLineCount(lineLengths, canvasWidth), terminalHeight);
            }
            else
            {
                canvasHeight = Math.Min(terminalHeight, inputHeight);
            }
        }

        return (canvasHeight, canvasWidth);
    }

    internal static long WrappedLineCount(IReadOnlyList<long> lineLengths, long width)
    {
        long count = 0;
        foreach (long length in lineLengths)
        {
            long remaining = length;
            while (remaining > width)
            {
                count += 1;
                remaining -= width;
            }

            count += 1;
        }

        return count;
    }

    /// <summary>Terminal._wrap_lines.</summary>
    internal static List<List<CharId>> WrapLines(List<List<CharId>> lines, long width)
    {
        var wrapped = new List<List<CharId>>();
        foreach (List<CharId> line in lines)
        {
            List<CharId> current = line;
            while (current.Count > width)
            {
                int split = (int)width;
                var rest = current.GetRange(split, current.Count - split);
                current.RemoveRange(split, current.Count - split);
                wrapped.Add(current);
                current = rest;
            }

            wrapped.Add(current);
        }

        return wrapped;
    }

    internal static (long ColumnOffset, long RowOffset) CalcCanvasOffsets(
        TerminalConfig config,
        Canvas canvas,
        long terminalWidth,
        long terminalHeight)
    {
        long columnOffset = 0;
        long rowOffset = 0;
        switch (config.AnchorCanvas)
        {
            case Anchor.S:
            case Anchor.N:
            case Anchor.C:
                columnOffset = PyCompat.FloorDiv(terminalWidth, 2) - PyCompat.FloorDiv(canvas.Width, 2);
                break;
            case Anchor.Se:
            case Anchor.E:
            case Anchor.Ne:
                columnOffset = terminalWidth - canvas.Width;
                break;
        }

        switch (config.AnchorCanvas)
        {
            case Anchor.W:
            case Anchor.E:
            case Anchor.C:
                rowOffset = PyCompat.FloorDiv(terminalHeight, 2) - PyCompat.FloorDiv(canvas.Height, 2);
                break;
            case Anchor.Nw:
            case Anchor.N:
            case Anchor.Ne:
                rowOffset = terminalHeight - canvas.Height;
                break;
        }

        return (columnOffset, rowOffset);
    }

    /// <summary>
    /// Terminal._setup_input_characters: wrap, assign 1-based bottom-up coords,
    /// drop plain spaces (they become fill), anchor, and keep in-canvas chars.
    /// </summary>
    internal static List<CharId> SetupInputCharacters(
        TerminalConfig config,
        Canvas canvas,
        List<EffectCharacter> arena,
        List<List<CharId>> preprocessedLines)
    {
        List<List<CharId>> formattedLines = config.WrapText
            ? WrapLines(preprocessedLines, canvas.Right)
            : preprocessedLines;
        long inputHeight = formattedLines.Count;
        var inputCharacters = new List<CharId>();
        for (int row = 0; row < formattedLines.Count; row++)
        {
            List<CharId> line = formattedLines[row];
            for (int column0 = 0; column0 < line.Count; column0++)
            {
                CharId id = line[column0];
                long column = column0 + 1L;
                EffectCharacter ch = arena[(int)id.Value];
                ch.InputCoord = Coord.New(column, inputHeight - row);
                if (ch.InputSymbol != " "
                    || ch.Animation.InputFgColor is not null
                    || ch.Animation.InputBgColor is not null)
                {
                    inputCharacters.Add(id);
                }
            }
        }

        return canvas.AnchorText(arena, inputCharacters, config.AnchorText);
    }
}
