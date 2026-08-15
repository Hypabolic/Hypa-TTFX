using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;

namespace Ttfx.Tests;

/// <summary>
/// Registry-driven CLI contract. Per-effect default/flag snapshots are redundant
/// with <c>tools/parity/cases.txt</c> <c>-basic</c>/<c>-custom</c> dumps; this
/// file asserts every registered option of every effect instead.
/// </summary>
internal static class EffectCliContractTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("registry 37 names in EffectCommand order", RegistryOrder);
        yield return new TestCase("every effect parses defaults and constructs", DefaultsAndFactories);
        yield return new TestCase("AtLeastOne flags replace defaults", FlagsReplaceDefaults);
        yield return new TestCase("every AtLeastOne option accepts multiple values", MultiValueOptions);
        yield return new TestCase("presence flags absent by default", PresenceFlags);
    }

    private static readonly string[] ExpectedNames =
    [
        "beams", "binarypath", "blackhole", "bouncyballs", "bubbles", "burn",
        "colorshift", "crumble", "decrypt", "errorcorrect", "expand", "fireworks",
        "highlight", "laseretch", "matrix", "middleout", "orbittingvolley", "overflow",
        "pour", "print", "rain", "randomsequence", "rings", "scattered",
        "slice", "slide", "smoke", "spotlights", "spray", "swarm",
        "sweep", "synthgrid", "thunderstorm", "unstable", "vhstape", "waves",
        "wipe",
    ];

    private static void RegistryOrder()
    {
        Harness.AssertEqual("count", 37, EffectRegistry.Effects.Length);
        for (int i = 0; i < ExpectedNames.Length; i++)
        {
            Harness.AssertEqual($"name[{i}]", ExpectedNames[i], EffectRegistry.Effects[i].Name);
            Harness.AssertTrue($"{ExpectedNames[i]} factory", EffectRegistry.Effects[i].Factory is not null);
        }

        Harness.AssertTrue("no probe", !EffectRegistry.Contains("probe"));
    }

    private static void DefaultsAndFactories()
    {
        foreach (EffectSpec effect in EffectRegistry.Effects)
        {
            ParseResult r = CliParser.Parse([effect.Name]);
            Harness.AssertEqual($"{effect.Name} parsed", effect.Name, r.EffectName);
            foreach (OptionSpec spec in effect.Options)
            {
                if (spec.Arity.Kind == OptionArityKind.Flag)
                {
                    Harness.AssertTrue(
                        $"{effect.Name} {spec.Long} absent",
                        !r.EffectOptions.ContainsKey(spec.Long));
                    continue;
                }

                if (spec.Default is not null && spec.Arity.Kind == OptionArityKind.One)
                {
                    Harness.AssertEqual(
                        $"{effect.Name} {spec.Long}",
                        spec.Parse(spec.Default),
                        r.EffectOptions[spec.Long]);
                    continue;
                }

                if (spec.Arity.Kind == OptionArityKind.AtLeastOne
                    && spec.DefaultValues is { Length: > 0 })
                {
                    var list = (List<object>)r.EffectOptions[spec.Long];
                    Harness.AssertEqual(
                        $"{effect.Name} {spec.Long} count",
                        spec.DefaultValues.Length,
                        list.Count);
                    for (int i = 0; i < spec.DefaultValues.Length; i++)
                    {
                        Harness.AssertEqual(
                            $"{effect.Name} {spec.Long}[{i}]",
                            spec.Parse(spec.DefaultValues[i]),
                            list[i]);
                    }

                    continue;
                }

                Harness.AssertTrue(
                    $"{effect.Name} {spec.Long} unexpected default shape",
                    false);
            }

            Harness.AssertTrue($"{effect.Name} FromOptions", effect.Factory!(r.EffectOptions) is not null);
        }
    }

    private static void FlagsReplaceDefaults()
    {
        foreach (EffectSpec effect in EffectRegistry.Effects)
        {
            foreach (OptionSpec spec in effect.Options)
            {
                if (spec.Arity.Kind != OptionArityKind.AtLeastOne)
                {
                    continue;
                }

                string token = SampleToken(spec);
                ParseResult r = CliParser.Parse([effect.Name, spec.Long, token]);
                var list = (List<object>)r.EffectOptions[spec.Long];
                Harness.AssertEqual($"{effect.Name} {spec.Long} replaced count", 1, list.Count);
                Harness.AssertEqual(
                    $"{effect.Name} {spec.Long} replaced value",
                    spec.Parse(token),
                    list[0]);
            }
        }
    }

    private static void MultiValueOptions()
    {
        foreach (EffectSpec effect in EffectRegistry.Effects)
        {
            foreach (OptionSpec spec in effect.Options)
            {
                if (spec.Arity.Kind != OptionArityKind.AtLeastOne)
                {
                    continue;
                }

                string a = SampleToken(spec);
                string b = SampleTokenAlt(spec);
                ParseResult r = CliParser.Parse([effect.Name, spec.Long, a, b]);
                var list = (List<object>)r.EffectOptions[spec.Long];
                Harness.AssertEqual($"{effect.Name} {spec.Long} multi count", 2, list.Count);
                Harness.AssertEqual($"{effect.Name} {spec.Long}[0]", spec.Parse(a), list[0]);
                Harness.AssertEqual($"{effect.Name} {spec.Long}[1]", spec.Parse(b), list[1]);
            }
        }
    }

    private static void PresenceFlags()
    {
        foreach (EffectSpec effect in EffectRegistry.Effects)
        {
            foreach (OptionSpec spec in effect.Options)
            {
                if (spec.Arity.Kind != OptionArityKind.Flag)
                {
                    continue;
                }

                ParseResult flagged = CliParser.Parse([effect.Name, spec.Long]);
                Harness.AssertEqual(
                    $"{effect.Name} {spec.Long} set",
                    true,
                    flagged.EffectOptions[spec.Long]);
            }
        }
    }

    private static string SampleToken(OptionSpec spec)
    {
        if (spec.Parse == ValueParsers.ParseColorArg)
        {
            return "ff0000";
        }

        if (spec.Parse == ValueParsers.ParseSymbol)
        {
            return "a";
        }

        if (spec.Parse == ValueParsers.ParseString)
        {
            return "x";
        }

        if (spec.Parse == ValueParsers.ParseCommonPositiveInt
            || spec.Parse == ValueParsers.ParseGradientSteps
            || spec.Parse == ValueParsers.ParsePositiveInt)
        {
            return "2";
        }

        if (spec.Parse == ValueParsers.ParseNonNegativeInt
            || spec.Parse == ValueParsers.ParseCommonNonNegativeInt)
        {
            return "0";
        }

        if (spec.Parse == ValueParsers.ParseNonNegativeRatio
            || spec.Parse == ValueParsers.ParsePositiveRatio)
        {
            return "0.5";
        }

        if (spec.Parse == ValueParsers.ParsePositiveFloat
            || spec.Parse == ValueParsers.ParseNonNegativeFloat)
        {
            return "1.0";
        }

        if (spec.Parse == ValueParsers.ParseEasingName)
        {
            return "in_sine";
        }

        if (spec.Parse == ValueParsers.ParsePositiveIntRange
            || spec.Parse == ValueParsers.ParsePositiveFloatRange)
        {
            return "1-2";
        }

        if (spec.Parse == ValueParsers.ParseGradientDirection)
        {
            return "horizontal";
        }

        if (spec.Parse == ValueParsers.ParseWaveDirection
            || spec.Parse == ValueParsers.ParseCharacterGroup)
        {
            return "column_left_to_right";
        }

        return "1";
    }

    private static string SampleTokenAlt(OptionSpec spec)
    {
        if (spec.Parse == ValueParsers.ParseColorArg)
        {
            return "00ff00";
        }

        if (spec.Parse == ValueParsers.ParseSymbol)
        {
            return "b";
        }

        if (spec.Parse == ValueParsers.ParseString)
        {
            return "y";
        }

        if (spec.Parse == ValueParsers.ParseEasingName)
        {
            return "out_sine";
        }

        if (spec.Parse == ValueParsers.ParsePositiveIntRange
            || spec.Parse == ValueParsers.ParsePositiveFloatRange)
        {
            return "3-4";
        }

        if (spec.Parse == ValueParsers.ParseGradientDirection)
        {
            return "vertical";
        }

        if (spec.Parse == ValueParsers.ParseWaveDirection
            || spec.Parse == ValueParsers.ParseCharacterGroup)
        {
            return "row_top_to_bottom";
        }

        if (spec.Parse == ValueParsers.ParseNonNegativeRatio
            || spec.Parse == ValueParsers.ParsePositiveRatio)
        {
            return "0.25";
        }

        if (spec.Parse == ValueParsers.ParsePositiveFloat
            || spec.Parse == ValueParsers.ParseNonNegativeFloat)
        {
            return "2.0";
        }

        if (spec.Parse == ValueParsers.ParseNonNegativeInt
            || spec.Parse == ValueParsers.ParseCommonNonNegativeInt)
        {
            return "1";
        }

        return "3";
    }
}
