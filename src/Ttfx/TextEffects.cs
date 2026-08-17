using System;
using System.Collections.Generic;
using System.IO;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx;

/// <summary>
/// Canvas, color, and RNG settings for <see cref="TextEffects"/>.
/// Defaults favor headless use: the canvas sizes to the input and the
/// process TTY is ignored. Pass CLI-form tokens in
/// <see cref="EffectArguments"/> for per-effect flags
/// (for example <c>--wipe-direction column_left_to_right</c>).
/// </summary>
public sealed class TextEffectOptions
{
    public ulong? Seed { get; init; }
    public long FrameRate { get; init; } = 60;
    public long CanvasWidth { get; init; } = -1;
    public long CanvasHeight { get; init; } = -1;
    public bool IgnoreTerminalDimensions { get; init; } = true;
    public bool WrapText { get; init; }
    public bool NoColor { get; init; }
    public bool XtermColors { get; init; }
    public long TabWidth { get; init; } = 4;
    public Color TerminalBackgroundColor { get; init; } = Color.FromHex("000000");
    public ExistingColorHandling ExistingColorHandling { get; init; } = ExistingColorHandling.Ignore;
    public Anchor AnchorCanvas { get; init; } = Anchor.Sw;
    public Anchor AnchorText { get; init; } = Anchor.Sw;
    public bool NoEol { get; init; }
    public bool NoRestoreCursor { get; init; }
    public bool ReuseCanvas { get; init; }
    public IReadOnlyList<string>? EffectArguments { get; init; }
}

/// <summary>
/// Host API for other .NET programs. The CLI remains the parity surface;
/// this type assembles the same engine objects the CLI does.
/// </summary>
public static class TextEffects
{
    public static IReadOnlyList<string> Names { get; } = CreateNames();

    public static bool Exists(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return EffectRegistry.Contains(name);
    }

    /// <summary>
    /// Build every frame with a virtual clock (no TTY, no sleep).
    /// <paramref name="maxFrames"/> caps the list; null means run to completion.
    /// </summary>
    public static IReadOnlyList<string> Render(
        string effect,
        string input,
        TextEffectOptions? options = null,
        ulong? maxFrames = null)
    {
        if (maxFrames == 0UL)
        {
            Prepare(effect, input, options, virtualClock: true);
            return Array.Empty<string>();
        }

        var frames = new List<string>();
        foreach (string frame in EnumerateFrames(effect, input, options))
        {
            frames.Add(frame);
            if (maxFrames is ulong limit && (ulong)frames.Count >= limit)
            {
                break;
            }
        }

        return frames;
    }

    /// <summary>Yield frames lazily with a virtual clock.</summary>
    public static IEnumerable<string> EnumerateFrames(
        string effect,
        string input,
        TextEffectOptions? options = null)
    {
        Session session = Prepare(effect, input, options, virtualClock: true);
        session.Effect.Build(session.World);
        while (true)
        {
            string? frame = session.Effect.NextFrame(session.World);
            if (frame is null)
            {
                yield break;
            }

            yield return frame;
        }
    }

    /// <summary>
    /// Animate onto <paramref name="stdout"/> with a real clock. When
    /// <paramref name="stdout"/> is null, writes to the process stdout.
    /// Does not install signal handlers — that stays a CLI concern.
    /// </summary>
    public static RunOutcome Run(
        string effect,
        string input,
        Stream? stdout = null,
        TextEffectOptions? options = null)
    {
        Session session = Prepare(effect, input, options, virtualClock: false);
        if (stdout is null)
        {
            return EffectRunner.RunEffect(session.Effect, session.World);
        }

        return EffectRunner.RunEffect(session.Effect, session.World, stdout);
    }

    private readonly struct Session
    {
        public Session(IEffect effect, EngineWorld world)
        {
            Effect = effect;
            World = world;
        }

        public IEffect Effect { get; }
        public EngineWorld World { get; }
    }

    private static Session Prepare(string effect, string input, TextEffectOptions? options, bool virtualClock)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(input);
        if (input.Trim().Length == 0)
        {
            throw new ArgumentException("NO INPUT.", nameof(input));
        }

        options ??= new TextEffectOptions();
        ParseResult parsed;
        try
        {
            var tokens = new List<string> { effect };
            if (options.EffectArguments is not null)
            {
                foreach (string argument in options.EffectArguments)
                {
                    tokens.Add(argument);
                }
            }

            parsed = CliParser.Parse(tokens);
        }
        catch (UsageError ex)
        {
            throw new ArgumentException(ex.Message, nameof(effect), ex);
        }

        if (parsed.EffectName is null)
        {
            throw new ArgumentException($"unrecognized effect '{effect}'", nameof(effect));
        }

        EffectSpec spec = EffectRegistry.Find(parsed.EffectName)!;
        if (spec.Factory is null)
        {
            throw new ArgumentException($"failed to build effect '{parsed.EffectName}'", nameof(effect));
        }

        InputParser.RejectUnsupported(input);
        Apply(parsed.Root, options);
        Rng rng = parsed.Root.Seed is ulong seed ? Rng.Seeded(seed) : Rng.FromEntropy();
        TerminalConfig config = TerminalConfig.FromRoot(parsed.Root);
        Clock clock = virtualClock
            ? Clock.VirtualWithFrameRate(config.FrameRate)
            : Clock.MakeReal();
        EngineWorld world = EngineWorld.New(input, config, rng, clock);
        return new Session(spec.Factory(parsed.EffectOptions), world);
    }

    private static void Apply(RootOptions root, TextEffectOptions options)
    {
        if (options.Seed is ulong seed)
        {
            root.Seed = seed;
        }

        root.FrameRate = options.FrameRate;
        root.CanvasWidth = options.CanvasWidth;
        root.CanvasHeight = options.CanvasHeight;
        root.IgnoreTerminalDimensions = options.IgnoreTerminalDimensions;
        root.WrapText = options.WrapText;
        root.NoColor = options.NoColor;
        root.XtermColors = options.XtermColors;
        root.TabWidth = options.TabWidth;
        root.TerminalBackgroundColor = options.TerminalBackgroundColor;
        root.ExistingColorHandling = options.ExistingColorHandling;
        root.AnchorCanvas = options.AnchorCanvas;
        root.AnchorText = options.AnchorText;
        root.NoEol = options.NoEol;
        root.NoRestoreCursor = options.NoRestoreCursor;
        root.ReuseCanvas = options.ReuseCanvas;
    }

    private static string[] CreateNames()
    {
        EffectSpec[] effects = EffectRegistry.Effects;
        var names = new string[effects.Length];
        for (int i = 0; i < effects.Length; i++)
        {
            names[i] = effects[i].Name;
        }

        return names;
    }
}
