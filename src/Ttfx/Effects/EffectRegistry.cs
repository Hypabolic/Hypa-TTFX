using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;

namespace Ttfx.Effects;

public sealed record EffectSpec(
    string Name,
    string Description,
    OptionSpec[] Options,
    Func<Dictionary<string, object>, IEffect>? Factory = null);

/// <summary>
/// Static name → option specs + factory. Enumeration order is observable:
/// <c>--random-effect</c> selects by <c>ChoiceIndex(names.Count)</c>, so the
/// list matches the reference EffectCommand order exactly.
/// <c>--probe</c> is a root flag, not a registry entry.
/// </summary>
public static class EffectRegistry
{
    public static EffectSpec[] Effects { get; } =
    [
        new EffectSpec(
            "beams",
            "Create beams which travel over the canvas illuminating the characters behind them.",
            [
                new OptionSpec(
                    "--beam-row-symbols",
                    null,
                    "SYMBOL",
                    "Symbols to use for the beam effect when moving along a row.",
                    OptionArity.AtLeastOne,
                    null,
                    ValueParsers.ParseSymbol),
                new OptionSpec(
                    "--beam-column-symbols",
                    null,
                    "SYMBOL",
                    "Symbols to use for the beam effect when moving along a column.",
                    OptionArity.AtLeastOne,
                    null,
                    ValueParsers.ParseSymbol),
            ]),
        Stub("binarypath", "Binary representations of each character move towards the home coordinate of the character."),
        Stub("blackhole", "Characters are consumed by a black hole and explode outwards."),
        Stub("bouncyballs", "Characters are bouncy balls falling from the top of the canvas."),
        Stub("bubbles", "Characters are formed into bubbles that float down and pop."),
        Stub("burn", "Burns vertically in the canvas."),
        Stub("colorshift", "Display a gradient that shifts colors across the terminal."),
        Stub("crumble", "Characters lose color and crumble into dust, vacuumed up, and reformed."),
        Stub("decrypt", "Display a movie style decryption effect."),
        Stub("errorcorrect", "Some characters start in the wrong position and are corrected in sequence."),
        Stub("expand", "Expands the text from a single point."),
        Stub("fireworks", "Characters launch and explode like fireworks and fall into place."),
        Stub("highlight", "Run a specular highlight across the text."),
        Stub("laseretch", "A laser etches characters onto the terminal."),
        Stub("matrix", "Matrix digital rain effect."),
        Stub("middleout", "Text expands in a single row or column in the middle of the canvas then out."),
        Stub(
            "orbittingvolley",
            "Four launchers orbit the canvas firing volleys of characters inward to build the input text from the center out."),
        Stub(
            "overflow",
            "Input text overflows and scrolls the terminal in a random order until eventually appearing ordered."),
        Stub("pour", "Pours the characters into position from the given direction."),
        Stub("print", "Lines are printed one at a time following a print head. Print head performs line feed, carriage return."),
        Stub("rain", "Rain characters from the top of the canvas."),
        Stub("randomsequence", "Prints the input data in a random sequence."),
        Stub("rings", "Characters are dispersed and form into spinning rings."),
        Stub("scattered", "Text is scattered across the canvas and moves into position."),
        Stub("slice", "Slices the input in half and slides it into place from opposite directions."),
        Stub("slide", "Slide characters into view from outside the terminal."),
        Stub("smoke", "Smoke floods the canvas colorizing any characters it crosses."),
        Stub(
            "spotlights",
            "Spotlights search the text area, illuminating characters, before converging in the center and expanding."),
        Stub("spray", "Draws the characters spawning at varying rates from a single point."),
        Stub("swarm", "Characters are grouped into swarms and move around the terminal before settling into position."),
        Stub("sweep", "Sweep across the canvas to reveal uncolored text, reverse sweep to color the text."),
        Stub("synthgrid", "Create a grid which fills with characters dissolving into the final text."),
        Stub("thunderstorm", "Create a thunderstorm in the terminal."),
        Stub(
            "unstable",
            "Spawn characters jumbled, explode them to the edge of the canvas, then reassemble them in the correct layout."),
        Stub("vhstape", "Lines of characters glitch left and right and lose detail like an old VHS tape."),
        Stub("waves", "Waves travel across the terminal leaving behind the characters."),
        new EffectSpec(
            "wipe",
            "Wipes the text across the terminal to reveal characters.",
            [
                new OptionSpec(
                    "--wipe-direction",
                    null,
                    "DIRECTION",
                    "Direction the text will wipe.",
                    OptionArity.One,
                    "diagonal_top_left_to_bottom_right",
                    ValueParsers.ParseCharacterGroup),
                new OptionSpec(
                    "--wipe-delay",
                    null,
                    "DELAY",
                    "Number of frames to wait before adding the next character group.",
                    OptionArity.One,
                    "0",
                    ValueParsers.ParseCommonNonNegativeInt),
                new OptionSpec(
                    "--wipe-ease",
                    null,
                    "EASE",
                    "Easing function to use for the wipe effect.",
                    OptionArity.One,
                    "in_out_circ",
                    ValueParsers.ParseEasingName),
                new OptionSpec(
                    "--final-gradient-stops",
                    null,
                    "COLOR",
                    "Space separated, unquoted, list of colors for the wipe gradient.",
                    OptionArity.AtLeastOne,
                    null,
                    ValueParsers.ParseColorArg,
                    DefaultValues: ["833ab4", "fd1d1d", "fcb045"]),
                new OptionSpec(
                    "--final-gradient-steps",
                    null,
                    "STEPS",
                    "Number of gradient steps to use.",
                    OptionArity.AtLeastOne,
                    null,
                    ValueParsers.ParseGradientSteps,
                    DefaultValues: ["12"]),
                new OptionSpec(
                    "--final-gradient-frames",
                    null,
                    "FRAMES",
                    "Number of frames to display each gradient step.",
                    OptionArity.One,
                    "3",
                    ValueParsers.ParseI64Object),
                new OptionSpec(
                    "--final-gradient-direction",
                    null,
                    "DIRECTION",
                    "Direction of the final gradient.",
                    OptionArity.One,
                    "vertical",
                    ValueParsers.ParseGradientDirection),
            ],
            Wipe.FromOptions),
    ];

    public static EffectSpec? Find(string name)
    {
        foreach (EffectSpec spec in Effects)
        {
            if (spec.Name == name)
            {
                return spec;
            }
        }

        return null;
    }

    public static bool Contains(string name) => Find(name) is not null;

    private static EffectSpec Stub(string name, string description) =>
        new EffectSpec(name, description, []);
}
