using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Handling of preexisting SGR colors in the input (TerminalConfig option).
/// </summary>
public enum ExistingColorHandling
{
    Always,
    Dynamic,
    Ignore,
}

public enum SyncMetric
{
    Distance,
    Step,
}

/// <summary>
/// The precomputed ANSI string for one cell, stored as UTF-8 bytes.
/// Representation half of Rust's inline/heap union is dropped (plan §5.8);
/// the cached byte[] is the semantic half.
/// Transcribed from <c>engine/animation.rs</c>.
/// </summary>
public sealed class FormattedSymbol
{
    private readonly byte[] _bytes;

    public FormattedSymbol(byte[] bytes)
    {
        _bytes = bytes;
    }

    public static FormattedSymbol New(string text)
    {
        return new FormattedSymbol(Encoding.UTF8.GetBytes(text));
    }

    public byte[] Bytes => _bytes;

    public string AsStr() => Encoding.UTF8.GetString(_bytes);

    public void AppendTo(System.Buffers.ArrayBufferWriter<byte> outBuf)
    {
        outBuf.Write(_bytes);
    }

    public void AppendTo(System.Collections.Generic.List<byte> outBuf)
    {
        outBuf.AddRange(_bytes);
    }
}

public sealed class VisualParams
{
    public bool Bold { get; set; }
    public bool Dim { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Blink { get; set; }
    public bool Reverse { get; set; }
    public bool Hidden { get; set; }
    public bool Strike { get; set; }
    public ColorPair? Colors { get; set; }
    public Ansi.ColorCode? FgColorCode { get; set; }
    public Ansi.ColorCode? BgColorCode { get; set; }
}

/// <summary>
/// animation.CharacterVisual with the formatted ANSI string precomputed.
/// </summary>
public sealed class CharacterVisual
{
    public string Symbol { get; }
    public bool Bold { get; }
    public bool Dim { get; } // stored but never emitted, faithfully
    public bool Italic { get; }
    public bool Underline { get; }
    public bool Blink { get; }
    public bool Reverse { get; }
    public bool Hidden { get; }
    public bool Strike { get; }
    public ColorPair? Colors { get; }
    public Ansi.ColorCode? FgColorCode { get; }
    public Ansi.ColorCode? BgColorCode { get; }
    public FormattedSymbol FormattedSymbol { get; }

    private static readonly StringBuilder FormatScratch = new StringBuilder();

    public CharacterVisual(string symbol, VisualParams p)
    {
        Symbol = symbol;
        Bold = p.Bold;
        Dim = p.Dim;
        Italic = p.Italic;
        Underline = p.Underline;
        Blink = p.Blink;
        Reverse = p.Reverse;
        Hidden = p.Hidden;
        Strike = p.Strike;
        Colors = p.Colors;
        FgColorCode = p.FgColorCode;
        BgColorCode = p.BgColorCode;
        // Effects rebuild visuals every frame, so the SGR string is assembled in
        // a reused scratch buffer rather than a fresh allocation per visual.
        FormatScratch.Clear();
        FormatSymbolInto(FormatScratch);
        FormattedSymbol = FormattedSymbol.New(FormatScratch.ToString());
    }

    public static CharacterVisual New(string symbol, VisualParams p) => new CharacterVisual(symbol, p);

    public static CharacterVisual Plain(string symbol) => new CharacterVisual(symbol, new VisualParams());

    /// <summary>
    /// SGR emission in upstream's fixed order; <c>dim</c> intentionally omitted;
    /// bare symbol when nothing applies.
    /// </summary>
    private void FormatSymbolInto(StringBuilder fmt)
    {
        if (Bold)
        {
            fmt.Append(Ansi.Bold);
        }

        if (Italic)
        {
            fmt.Append(Ansi.Italic);
        }

        if (Underline)
        {
            fmt.Append(Ansi.Underline);
        }

        if (Blink)
        {
            fmt.Append(Ansi.Blink);
        }

        if (Reverse)
        {
            fmt.Append(Ansi.Reverse);
        }

        if (Hidden)
        {
            fmt.Append(Ansi.Hidden);
        }

        if (Strike)
        {
            fmt.Append(Ansi.Strikethrough);
        }

        if (FgColorCode is not null)
        {
            Ansi.Fg(FgColorCode, fmt);
        }

        if (BgColorCode is not null)
        {
            Ansi.Bg(BgColorCode, fmt);
        }

        fmt.Append(Symbol);
        // Rust str::len() is bytes. Compare UTF-8 byte counts, not String.Length.
        if (Encoding.UTF8.GetByteCount(fmt.ToString()) != Encoding.UTF8.GetByteCount(Symbol))
        {
            fmt.Append(Ansi.ResetAll);
        }
    }
}

/// <summary>
/// animation.Frame. Frames live in Scene.all_frames (stable storage);
/// Scene.frames / Scene.played_frames hold indices into it, preserving the
/// upstream object-identity semantics of frame_index_map.
/// </summary>
public sealed class Frame
{
    public CharacterVisual CharacterVisual { get; }
    public long Duration { get; }
    public long TicksElapsed { get; set; }

    public Frame(CharacterVisual characterVisual, long duration)
    {
        CharacterVisual = characterVisual;
        Duration = duration;
        TicksElapsed = 0;
    }
}

/// <summary>
/// animation.Scene.
/// Transcribed from <c>engine/animation.rs</c>.
/// </summary>
public sealed class Scene
{
    public string SceneId { get; }
    public bool IsLooping { get; }
    public SyncMetric? Sync { get; }
    public Easing? Ease { get; }
    public bool NoColor { get; }
    public bool UseXtermColors { get; }

    /// <summary>Stable frame storage; never reordered.</summary>
    public List<Frame> AllFrames { get; } = new List<Frame>();

    /// <summary>
    /// Remaining frame queue (indices into all_frames). FIFO: push_back / pop_front
    /// (<c>animation.rs:226</c>). List so synced-scene indexing (<c>ctx.rs:613</c>)
    /// and <c>.back()</c> (<c>ctx.rs:594</c>) work.
    /// </summary>
    public List<int> Frames { get; } = new List<int>();

    /// <summary>
    /// Played frames (indices into all_frames). Append-only list;
    /// <c>reset_scene</c> restores played+remaining in original order.
    /// </summary>
    public List<int> PlayedFrames { get; } = new List<int>();

    /// <summary>Tick index -&gt; frame index (upstream frame_index_map).</summary>
    public List<int> FrameIndexMap { get; } = new List<int>();

    public long EasingTotalSteps { get; set; }
    public long EasingCurrentStep { get; set; }
    public ColorPair? PreexistingColors { get; set; }
    public bool PreexistingBold { get; set; }

    public Scene(
        string sceneId,
        bool isLooping,
        SyncMetric? sync,
        Easing? ease,
        bool noColor,
        bool useXtermColors)
    {
        SceneId = sceneId;
        IsLooping = isLooping;
        Sync = sync;
        Ease = ease;
        NoColor = noColor;
        UseXtermColors = useXtermColors;
    }

    public static Scene New(
        string sceneId,
        bool isLooping,
        SyncMetric? sync,
        Easing? ease,
        bool noColor,
        bool useXtermColors) =>
        new Scene(sceneId, isLooping, sync, ease, noColor, useXtermColors);

    /// <summary>
    /// Scene._get_color_code. Upstream memoizes into a process-global ClassVar
    /// dict; the memo is value-transparent so we just recompute.
    /// </summary>
    private Ansi.ColorCode? GetColorCode(Color? color)
    {
        if (color is null)
        {
            return null;
        }

        if (NoColor)
        {
            return null;
        }

        if (UseXtermColors)
        {
            if (color.XtermColor is byte code)
            {
                return new Ansi.ColorCode.Xterm(code);
            }

            return new Ansi.ColorCode.Xterm(Hexterm.HexToXterm(color.RgbColor));
        }

        return new Ansi.ColorCode.Rgb(color.RgbColor);
    }

    /// <summary>Scene.add_frame with the preexisting-color/bold overrides.</summary>
    public void AddFrame(string symbol, long duration, VisualParams parameters)
    {
        if (PreexistingColors is ColorPair pre)
        {
            parameters.Colors = pre;
        }

        if (PreexistingBold)
        {
            parameters.Bold = true;
        }

        if (parameters.Colors is ColorPair colors)
        {
            parameters.FgColorCode = GetColorCode(colors.FgColor);
            parameters.BgColorCode = GetColorCode(colors.BgColor);
        }
        else
        {
            parameters.FgColorCode = null;
            parameters.BgColorCode = null;
        }

        if (duration < 1)
        {
            throw new EngineException($"Frame duration must be at least 1. Received: {duration}");
        }

        CharacterVisual visual = CharacterVisual.New(symbol, parameters);
        int frameIndex = AllFrames.Count;
        AllFrames.Add(new Frame(visual, duration));
        Frames.Add(frameIndex);
        for (long n = 0; n < duration; n++)
        {
            FrameIndexMap.Add(frameIndex);
            EasingTotalSteps += 1;
        }
    }

    /// <summary>Scene.activate: first frame's visual, error when empty.</summary>
    public CharacterVisual Activate()
    {
        if (Frames.Count == 0)
        {
            throw new EngineException($"Scene {SceneId} has no frames.");
        }

        return AllFrames[Frames[0]].CharacterVisual;
    }

    /// <summary>
    /// Scene.get_next_visual: tick the head frame, retiring it (and looping)
    /// exactly as upstream.
    /// </summary>
    public CharacterVisual GetNextVisual()
    {
        int head = Frames[0];
        CharacterVisual nextVisual = AllFrames[head].CharacterVisual;
        AllFrames[head].TicksElapsed += 1;
        if (AllFrames[head].TicksElapsed == AllFrames[head].Duration)
        {
            AllFrames[head].TicksElapsed = 0;
            PlayedFrames.Add(Frames[0]);
            Frames.RemoveAt(0);
            if (IsLooping && Frames.Count == 0)
            {
                Frames.AddRange(PlayedFrames);
                PlayedFrames.Clear();
            }
        }

        return nextVisual;
    }

    /// <summary>
    /// Scene.apply_gradient_to_symbols with the exact cyclic_distribution
    /// generator semantics (repeat factor + overflow-remainder rule).
    /// </summary>
    public void ApplyGradientToSymbols(
        IReadOnlyList<string> symbols,
        long duration,
        Gradient? fgGradient,
        Gradient? bgGradient)
    {
        static List<(T Larger, R Smaller)> CyclicDistribution<T, R>(IReadOnlyList<T> larger, IReadOnlyList<R> smaller)
        {
            int repeatFactor = larger.Count / smaller.Count;
            int overflowCount = larger.Count % smaller.Count;
            bool overflowUsed = false;
            int smallerIndex = 0;
            int currentRepeatFactor = 0;
            var output = new List<(T, R)>(larger.Count);
            // Length captured once: cyclic_distribution does not emit (animation.rs:349).
            int largerCount = larger.Count;
            for (int i = 0; i < largerCount; i++)
            {
                if (currentRepeatFactor >= repeatFactor)
                {
                    if (overflowCount > 0)
                    {
                        if (overflowUsed)
                        {
                            smallerIndex += 1;
                            currentRepeatFactor = 0;
                            overflowUsed = false;
                        }
                        else
                        {
                            overflowUsed = true;
                            overflowCount -= 1;
                        }
                    }
                    else
                    {
                        smallerIndex += 1;
                        currentRepeatFactor = 0;
                    }
                }

                currentRepeatFactor += 1;
                output.Add((larger[i], smaller[smallerIndex]));
            }

            return output;
        }

        bool fgHas = fgGradient is not null && fgGradient.Spectrum.Count > 0;
        bool bgHas = bgGradient is not null && bgGradient.Spectrum.Count > 0;
        if (fgGradient is null && bgGradient is null)
        {
            throw new EngineException(
                "Foreground and background gradient are None. At least one gradient must be provided.");
        }

        if (!fgHas && !bgHas)
        {
            throw new EngineException(
                "Foreground and background gradient are empty. At least one gradient must have at least one color.");
        }

        // Length captured once: symbol validation does not emit (animation.rs:382).
        int symbolCount = symbols.Count;
        for (int i = 0; i < symbolCount; i++)
        {
            if (Unicode.RuneCount(symbols[i]) > 1)
            {
                throw new EngineException($"Symbol must be a string with a length of 1. Received: `{symbols[i]}`.");
            }
        }

        var colorPairs = new List<ColorPair>();
        if (fgHas && bgHas)
        {
            IReadOnlyList<Color> fg = fgGradient!.Spectrum;
            IReadOnlyList<Color> bg = bgGradient!.Spectrum;
            if (fg.Count >= bg.Count)
            {
                List<(Color F, Color B)> pairs = CyclicDistribution(fg, bg);
                int pairCount = pairs.Count;
                for (int i = 0; i < pairCount; i++)
                {
                    colorPairs.Add(ColorPair.New(pairs[i].F, pairs[i].B));
                }
            }
            else
            {
                List<(Color B, Color F)> pairs = CyclicDistribution(bg, fg);
                int pairCount = pairs.Count;
                for (int i = 0; i < pairCount; i++)
                {
                    colorPairs.Add(ColorPair.New(pairs[i].F, pairs[i].B));
                }
            }
        }
        else if (fgHas)
        {
            IReadOnlyList<Color> spectrum = fgGradient!.Spectrum;
            int count = spectrum.Count;
            for (int i = 0; i < count; i++)
            {
                colorPairs.Add(ColorPair.New(spectrum[i], null));
            }
        }
        else
        {
            IReadOnlyList<Color> spectrum = bgGradient!.Spectrum;
            int count = spectrum.Count;
            for (int i = 0; i < count; i++)
            {
                colorPairs.Add(ColorPair.New(null, spectrum[i]));
            }
        }

        if (symbols.Count >= colorPairs.Count)
        {
            List<(string Symbol, ColorPair Colors)> pairs = CyclicDistribution(symbols, colorPairs);
            int pairCount = pairs.Count;
            for (int i = 0; i < pairCount; i++)
            {
                AddFrame(pairs[i].Symbol, duration, new VisualParams { Colors = pairs[i].Colors });
            }
        }
        else
        {
            List<(ColorPair Colors, string Symbol)> pairs = CyclicDistribution(colorPairs, symbols);
            int pairCount = pairs.Count;
            for (int i = 0; i < pairCount; i++)
            {
                AddFrame(pairs[i].Symbol, duration, new VisualParams { Colors = pairs[i].Colors });
            }
        }
    }

    /// <summary>
    /// Scene.reset_scene: restore played + remaining frames in original order
    /// (played first), zero tick counters and the easing step.
    /// </summary>
    public void ResetScene()
    {
        // Remaining frames get ticks_elapsed zeroed as they move to played;
        // already-played frames were zeroed when they retired.
        var remaining = new List<int>(Frames);
        Frames.Clear();
        // Length captured once: reset does not emit (animation.rs:425).
        int remainingCount = remaining.Count;
        for (int i = 0; i < remainingCount; i++)
        {
            int idx = remaining[i];
            AllFrames[idx].TicksElapsed = 0;
            PlayedFrames.Add(idx);
        }

        Frames.AddRange(PlayedFrames);
        PlayedFrames.Clear();
        EasingCurrentStep = 0;
    }
}

/// <summary>
/// engine/animation.py Animation: per-character animation state.
/// Transcribed from <c>engine/animation.rs</c>.
/// </summary>
public sealed class Animation
{
    public OrderedMap<Scene> Scenes { get; } = new OrderedMap<Scene>();
    public string? ActiveScene { get; set; }
    public bool UseXtermColors { get; set; }
    public bool NoColor { get; set; }
    public ExistingColorHandling ExistingColorHandling { get; set; }
    public Color? InputFgColor { get; set; }
    public Color? InputBgColor { get; set; }
    public bool InputBold { get; set; }
    public long ActiveSceneCurrentStep { get; set; }
    public CharacterVisual CurrentCharacterVisual { get; set; }

    private Animation(string inputSymbol)
    {
        UseXtermColors = false;
        NoColor = false;
        ExistingColorHandling = ExistingColorHandling.Ignore;
        InputFgColor = null;
        InputBgColor = null;
        InputBold = false;
        ActiveSceneCurrentStep = 0;
        CurrentCharacterVisual = CharacterVisual.Plain(inputSymbol);
    }

    public static Animation New(string inputSymbol) => new Animation(inputSymbol);

    /// <summary>
    /// Animation.new_scene: auto-ids are stringified integers probing upward;
    /// duplicate explicit ids silently overwrite (faithful).
    /// </summary>
    public string NewScene(
        bool isLooping,
        SyncMetric? sync,
        Easing? ease,
        string sceneId,
        bool usesInputPreexistingColors)
    {
        string resolvedId;
        if (sceneId.Length == 0)
        {
            int currentId = Scenes.Count;
            while (true)
            {
                string candidate = currentId.ToString(CultureInfo.InvariantCulture);
                if (!Scenes.ContainsKey(candidate))
                {
                    resolvedId = candidate;
                    break;
                }

                currentId += 1;
            }
        }
        else
        {
            resolvedId = sceneId;
        }

        ColorPair? preexistingColors = null;
        bool preexistingBold = false;
        if (ExistingColorHandling == ExistingColorHandling.Always && usesInputPreexistingColors)
        {
            preexistingColors = ColorPair.New(InputFgColor, InputBgColor);
            preexistingBold = InputBold;
        }

        Scene scene = Scene.New(resolvedId, isLooping, sync, ease, NoColor, UseXtermColors);
        scene.PreexistingColors = preexistingColors;
        scene.PreexistingBold = preexistingBold;
        Scenes.Insert(resolvedId, scene);
        return resolvedId;
    }

    /// <summary>Animation.active_scene_is_complete: no scene, no remaining frames, or looping.</summary>
    public bool ActiveSceneIsComplete()
    {
        if (ActiveScene is null)
        {
            return true;
        }

        Scene scene = Scenes.Get(ActiveScene) ?? throw new EngineInvariantException("active scene must exist");
        return scene.Frames.Count == 0 || scene.IsLooping;
    }

    /// <summary>Animation._get_color_code.</summary>
    public Ansi.ColorCode? GetColorCode(Color? color)
    {
        if (color is null)
        {
            return null;
        }

        if (NoColor)
        {
            return null;
        }

        if (UseXtermColors)
        {
            if (color.XtermColor is byte code)
            {
                return new Ansi.ColorCode.Xterm(code);
            }

            return new Ansi.ColorCode.Xterm(Hexterm.HexToXterm(color.RgbColor));
        }

        return new Ansi.ColorCode.Rgb(color.RgbColor);
    }

    /// <summary>Animation.set_appearance.</summary>
    public void SetAppearance(
        string inputSymbol,
        bool usesInputPreexistingColors,
        string? symbol,
        ColorPair? colors)
    {
        string resolvedSymbol = symbol ?? inputSymbol;
        ColorPair resolvedColors = colors ?? new ColorPair();
        bool bold = false;
        if (ExistingColorHandling == ExistingColorHandling.Always && usesInputPreexistingColors)
        {
            resolvedColors = ColorPair.New(InputFgColor, InputBgColor);
            bold = InputBold;
        }

        Ansi.ColorCode? fgCode = GetColorCode(resolvedColors.FgColor);
        Ansi.ColorCode? bgCode = GetColorCode(resolvedColors.BgColor);
        CurrentCharacterVisual = CharacterVisual.New(
            resolvedSymbol,
            new VisualParams
            {
                Bold = bold,
                Colors = resolvedColors,
                FgColorCode = fgCode,
                BgColorCode = bgCode,
            });
    }

    /// <summary>
    /// Animation.adjust_color_brightness: hand-rolled RGB-&gt;HSL-&gt;RGB with
    /// round() (banker's) at the end — unlike shift_color_towards's truncation.
    /// </summary>
    public static Color AdjustColorBrightness(Color color, double brightness)
    {
        static double HueToRgb(double lightnessScaled, double colorIntensity, double hueValue)
        {
            if (hueValue < 0.0)
            {
                hueValue += 1.0;
            }

            if (hueValue > 1.0)
            {
                hueValue -= 1.0;
            }

            if (hueValue < 1.0 / 6.0)
            {
                return lightnessScaled + (colorIntensity - lightnessScaled) * 6.0 * hueValue;
            }

            if (hueValue < 1.0 / 2.0)
            {
                return colorIntensity;
            }

            if (hueValue < 2.0 / 3.0)
            {
                return lightnessScaled + (colorIntensity - lightnessScaled) * (2.0 / 3.0 - hueValue) * 6.0;
            }

            return lightnessScaled;
        }

        (byte r, byte g, byte b) = color.RgbInts();
        double normalizedRed = r / 255.0;
        double normalizedGreen = g / 255.0;
        double normalizedBlue = b / 255.0;

        double maxVal = PyCompat.FMax(PyCompat.FMax(normalizedRed, normalizedGreen), normalizedBlue);
        double minVal = PyCompat.FMin(PyCompat.FMin(normalizedRed, normalizedGreen), normalizedBlue);
        double lightness = (maxVal + minVal) / 2.0;

        double lightnessThreshold = 0.5;
        double hueValue;
        double saturation;
        if (maxVal == minVal)
        {
            hueValue = 0.0;
            saturation = 0.0;
        }
        else
        {
            double diff = maxVal - minVal;
            saturation = lightness > lightnessThreshold
                ? diff / (2.0 - maxVal - minVal)
                : diff / (maxVal + minVal);
            if (maxVal == normalizedRed)
            {
                hueValue = (normalizedGreen - normalizedBlue) / diff + (normalizedGreen < normalizedBlue ? 6.0 : 0.0);
            }
            else if (maxVal == normalizedGreen)
            {
                hueValue = (normalizedBlue - normalizedRed) / diff + 2.0;
            }
            else
            {
                hueValue = (normalizedRed - normalizedGreen) / diff + 4.0;
            }

            hueValue /= 6.0;
        }

        lightness = PyCompat.FMax(PyCompat.FMin(lightness * brightness, 1.0), 0.0);

        double red;
        double green;
        double blue;
        if (saturation == 0.0)
        {
            red = lightness;
            green = lightness;
            blue = lightness;
        }
        else
        {
            double colorIntensity = lightness < lightnessThreshold
                ? lightness * (1.0 + saturation)
                : lightness + saturation - lightness * saturation;
            double lightnessScaled = 2.0 * lightness - colorIntensity;
            red = HueToRgb(lightnessScaled, colorIntensity, hueValue + 1.0 / 3.0);
            green = HueToRgb(lightnessScaled, colorIntensity, hueValue);
            blue = HueToRgb(lightnessScaled, colorIntensity, hueValue - 1.0 / 3.0);
        }

        string adjusted =
            $"{PyCompat.RoundHalfEven(red * 255.0):x2}{PyCompat.RoundHalfEven(green * 255.0):x2}{PyCompat.RoundHalfEven(blue * 255.0):x2}";
        return Color.FromHex(adjusted);
    }
}
