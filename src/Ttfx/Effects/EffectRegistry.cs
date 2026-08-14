using System;
using Ttfx.Cli;

namespace Ttfx.Effects;

public sealed record EffectSpec(string Name, string Description, OptionSpec[] Options);

/// <summary>
/// Static name → option specs. 0003 registers only the effects needed for
/// parser tests (wipe, beams). The 37-name order assertion is a later issue.
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
        new EffectSpec(
            "wipe",
            "Wipes the text across the terminal to reveal characters.",
            [
                new OptionSpec(
                    "--wipe-ease",
                    null,
                    "EASE",
                    "Easing function to use for the wipe effect.",
                    OptionArity.One,
                    "in_out_circ",
                    ValueParsers.ParseEasingName),
            ]),
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
}
