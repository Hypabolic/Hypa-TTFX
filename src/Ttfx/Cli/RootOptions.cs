using System.Collections.Generic;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Cli;

public sealed class RootOptions
{
    public bool Version { get; set; }
    public string? InputFile { get; set; }
    public long TabWidth { get; set; } = 4;
    public bool XtermColors { get; set; }
    public bool NoColor { get; set; }
    public Color TerminalBackgroundColor { get; set; } = ValueParsers.ColorArg("#000000");
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
    public ulong? Seed { get; set; }
    public string? PrintCompletion { get; set; }
    public bool RandomEffect { get; set; }
    public List<string> IncludeEffects { get; } = new List<string>();
    public List<string> ExcludeEffects { get; } = new List<string>();
    public bool M0Dump { get; set; }
    public bool ParityDump { get; set; }
    public ulong? MaxFrames { get; set; }
    public bool VirtualClock { get; set; }
    public bool Probe { get; set; }
    public bool EasingGoldenDump { get; set; }
    public bool GeometryGoldenDump { get; set; }
    /// <summary>Parity-only: rebuild after N dump frames (same path as SIGWINCH rebuild).</summary>
    public ulong? RebuildAfter { get; set; }

    public static OptionSpec[] Specs { get; } =
    [
        new OptionSpec("--version", 'v', "", "Print the version and exit", OptionArity.Flag, null, _ => true),
        new OptionSpec("--input-file", 'i', "PATH", "File to read input from", OptionArity.One, null, ValueParsers.ParseInputFile),
        new OptionSpec("--tab-width", null, "TAB_WIDTH", "", OptionArity.One, "4", ValueParsers.ParsePositiveInt),
        new OptionSpec("--xterm-colors", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--no-color", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--terminal-background-color", null, "COLOR", "", OptionArity.One, "#000000", ValueParsers.ParseColorArg),
        new OptionSpec("--existing-color-handling", null, "HANDLING", "", OptionArity.One, "ignore", ValueParsers.ParseExistingColorHandling),
        new OptionSpec("--wrap-text", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--frame-rate", null, "FRAME_RATE", "", OptionArity.One, "60", ValueParsers.ParseNonNegativeInt),
        new OptionSpec("--canvas-width", null, "CANVAS_WIDTH", "", OptionArity.One, "-1", ValueParsers.ParseCanvasDimension, AllowNegative: true),
        new OptionSpec("--canvas-height", null, "CANVAS_HEIGHT", "", OptionArity.One, "-1", ValueParsers.ParseCanvasDimension, AllowNegative: true),
        new OptionSpec("--anchor-canvas", null, "ANCHOR", "", OptionArity.One, "sw", ValueParsers.ParseAnchor),
        new OptionSpec("--anchor-text", null, "ANCHOR", "", OptionArity.One, "sw", ValueParsers.ParseAnchor),
        new OptionSpec("--ignore-terminal-dimensions", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--reuse-canvas", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--no-eol", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--no-restore-cursor", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--seed", null, "SEED", "Seed for the random number generator", OptionArity.One, null, ValueParsers.ParseSeed),
        new OptionSpec("--print-completion", null, "SHELL", "Print a shell completion script and exit", OptionArity.One, null, ValueParsers.ParseCompletionShell),
        new OptionSpec("--random-effect", 'R', "", "Run a random effect", OptionArity.Flag, null, _ => true),
        new OptionSpec("--include-effects", null, "EFFECT", "Limit random-effect selection", OptionArity.AtLeastOne, null, ValueParsers.ParseString),
        new OptionSpec("--exclude-effects", null, "EFFECT", "Exclude these effects from random-effect selection", OptionArity.AtLeastOne, null, ValueParsers.ParseString),
        new OptionSpec("--m0-dump", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--parity-dump", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--max-frames", null, "N", "", OptionArity.One, null, ValueParsers.ParseSeed),
        new OptionSpec("--virtual-clock", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--probe", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--easing-golden-dump", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--geometry-golden-dump", null, "", "", OptionArity.Flag, null, _ => true),
        new OptionSpec("--rebuild-after", null, "N", "Parity-only: rebuild after N dump frames", OptionArity.One, null, ValueParsers.ParseSeed),
    ];
}
