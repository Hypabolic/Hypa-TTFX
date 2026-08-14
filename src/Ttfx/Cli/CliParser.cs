using System;
using System.Collections.Generic;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Cli;

public sealed class ParseResult
{
    public ParseResult(RootOptions root, string? effectName, Dictionary<string, object> effectOptions)
    {
        Root = root;
        EffectName = effectName;
        EffectOptions = effectOptions;
    }

    public RootOptions Root { get; }
    public string? EffectName { get; }
    public Dictionary<string, object> EffectOptions { get; }
}

public static class CliParser
{
    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        var root = new RootOptions();
        var effectOptions = new Dictionary<string, object>(StringComparer.Ordinal);
        var seenRoot = new HashSet<string>(StringComparer.Ordinal);
        var seenEffect = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, OptionSpec> rootByLong = IndexByLong(RootOptions.Specs);
        Dictionary<char, OptionSpec> rootByShort = IndexByShort(RootOptions.Specs);

        string? effectName = null;
        Dictionary<string, OptionSpec> effectByLong = new Dictionary<string, OptionSpec>(StringComparer.Ordinal);
        Dictionary<char, OptionSpec> effectByShort = new Dictionary<char, OptionSpec>();
        bool optionsEnded = false;
        int i = 0;

        while (i < args.Count)
        {
            string token = args[i];

            if (!optionsEnded && token == "--")
            {
                optionsEnded = true;
                i++;
                continue;
            }

            if (!optionsEnded && IsOptionToken(token))
            {
                if (effectName is null)
                {
                    i = ConsumeOption(args, i, token, rootByLong, rootByShort, seenRoot, ApplyRoot, isRootPhase: true);
                }
                else
                {
                    string longName = LongNameOf(token);
                    if (rootByLong.ContainsKey(longName) || IsRootShort(token, rootByShort))
                    {
                        throw new UsageError($"unexpected argument '{token}'");
                    }

                    i = ConsumeOption(args, i, token, effectByLong, effectByShort, seenEffect, ApplyEffect, isRootPhase: false);
                }

                continue;
            }

            if (effectName is null)
            {
                if (optionsEnded)
                {
                    throw new UsageError($"unexpected argument '{token}'");
                }

                EffectSpec? spec = EffectRegistry.Find(token);
                if (spec is null)
                {
                    throw new UsageError($"unrecognized subcommand '{token}'");
                }

                effectName = spec.Name;
                effectByLong = IndexByLong(spec.Options);
                effectByShort = IndexByShort(spec.Options);
                i++;
                continue;
            }

            throw new UsageError($"unexpected argument '{token}'");
        }

        if (root.IncludeEffects.Count > 0 && root.ExcludeEffects.Count > 0)
        {
            throw new UsageError("the argument '--include-effects' cannot be used with '--exclude-effects'");
        }

        if (effectName is not null)
        {
            EffectSpec effect = EffectRegistry.Find(effectName)!;
            foreach (OptionSpec spec in effect.Options)
            {
                if (effectOptions.ContainsKey(spec.Long))
                {
                    continue;
                }

                if (spec.Default is not null && spec.Arity.Kind == OptionArityKind.One)
                {
                    effectOptions[spec.Long] = spec.Parse(spec.Default);
                }
                else if (spec.Arity.Kind == OptionArityKind.AtLeastOne
                    && spec.DefaultValues is { Length: > 0 })
                {
                    var list = new List<object>(spec.DefaultValues.Length);
                    foreach (string value in spec.DefaultValues)
                    {
                        list.Add(spec.Parse(value));
                    }

                    effectOptions[spec.Long] = list;
                }
            }
        }

        return new ParseResult(root, effectName, effectOptions);

        void ApplyRoot(OptionSpec spec, object value) => AssignRoot(root, spec, value);

        void ApplyEffect(OptionSpec spec, object value)
        {
            if (spec.Arity.Kind == OptionArityKind.AtLeastOne)
            {
                if (!effectOptions.TryGetValue(spec.Long, out object? existing) || existing is not List<object> list)
                {
                    list = new List<object>();
                    effectOptions[spec.Long] = list;
                }

                if (value is List<object> more)
                {
                    list.AddRange(more);
                }
                else
                {
                    list.Add(value);
                }
            }
            else
            {
                effectOptions[spec.Long] = value;
            }
        }
    }

    public static bool IsOptionToken(string token)
    {
        if (token.Length < 2 || token == "--")
        {
            return false;
        }

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            return true;
        }

        return token[0] == '-' && IsAsciiLetter(token[1]);
    }

    private static bool IsRootShort(string token, Dictionary<char, OptionSpec> rootByShort)
    {
        if (token.StartsWith("--", StringComparison.Ordinal) || token.Length < 2 || token[0] != '-')
        {
            return false;
        }

        return IsAsciiLetter(token[1]) && rootByShort.ContainsKey(token[1]);
    }

    private static string LongNameOf(string token)
    {
        if (!token.StartsWith("--", StringComparison.Ordinal))
        {
            return token;
        }

        int eq = token.IndexOf('=');
        return eq < 0 ? token : token.Substring(0, eq);
    }

    private static int ConsumeOption(
        IReadOnlyList<string> args,
        int i,
        string token,
        Dictionary<string, OptionSpec> byLong,
        Dictionary<char, OptionSpec> byShort,
        HashSet<string> seen,
        Action<OptionSpec, object> apply,
        bool isRootPhase)
    {
        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            string? attached = null;
            string name = token;
            int eq = token.IndexOf('=');
            if (eq >= 0)
            {
                name = token.Substring(0, eq);
                attached = token.Substring(eq + 1);
            }

            if (!byLong.TryGetValue(name, out OptionSpec? spec))
            {
                throw new UsageError($"unexpected argument '{token}'");
            }

            return ConsumeSpec(args, i, spec, attached, seen, apply);
        }

        return ConsumeShortCluster(args, i, token, byShort, seen, apply, isRootPhase);
    }

    private static int ConsumeShortCluster(
        IReadOnlyList<string> args,
        int i,
        string token,
        Dictionary<char, OptionSpec> byShort,
        HashSet<string> seen,
        Action<OptionSpec, object> apply,
        bool isRootPhase)
    {
        int pos = 1;
        while (pos < token.Length)
        {
            char c = token[pos];
            if (!IsAsciiLetter(c) || !byShort.TryGetValue(c, out OptionSpec? spec))
            {
                throw new UsageError($"unexpected argument '{token}'");
            }

            if (!isRootPhase && RootLooksUp(spec.Long))
            {
                throw new UsageError($"unexpected argument '{token}'");
            }

            pos++;
            if (spec.Arity.Kind == OptionArityKind.Flag)
            {
                NoteSeen(seen, spec);
                apply(spec, true);
                continue;
            }

            string? attached = null;
            if (pos < token.Length)
            {
                attached = token[pos] == '=' ? token.Substring(pos + 1) : token.Substring(pos);
            }

            return ConsumeSpec(args, i, spec, attached, seen, apply);
        }

        return i + 1;
    }

    private static bool RootLooksUp(string longName)
    {
        foreach (OptionSpec spec in RootOptions.Specs)
        {
            if (spec.Long == longName)
            {
                return true;
            }
        }

        return false;
    }

    private static int ConsumeSpec(
        IReadOnlyList<string> args,
        int i,
        OptionSpec spec,
        string? attached,
        HashSet<string> seen,
        Action<OptionSpec, object> apply)
    {
        if (spec.Arity.Kind == OptionArityKind.Flag)
        {
            if (attached is not null)
            {
                throw new UsageError($"unexpected value '{attached}' for '{spec.Long}'");
            }

            NoteSeen(seen, spec);
            apply(spec, true);
            return i + 1;
        }

        if (spec.Arity.Kind == OptionArityKind.AtLeastOne)
        {
            var values = new List<object>();
            int next = i + 1;
            if (attached is not null)
            {
                values.Add(spec.Parse(attached));
            }

            while (next < args.Count)
            {
                string candidate = args[next];
                if (candidate == "--" || IsOptionToken(candidate))
                {
                    break;
                }

                values.Add(spec.Parse(candidate));
                next++;
            }

            if (values.Count == 0)
            {
                throw new UsageError($"a value is required for '{spec.Long}' but none was supplied");
            }

            apply(spec, values);
            return next;
        }

        int take = spec.Arity.Kind == OptionArityKind.Exactly ? spec.Arity.Count : 1;
        var taken = new List<object>(take);
        int cursor = i + 1;
        if (attached is not null)
        {
            taken.Add(spec.Parse(attached));
        }

        while (taken.Count < take)
        {
            if (cursor >= args.Count)
            {
                throw new UsageError($"a value is required for '{spec.Long}' but none was supplied");
            }

            string candidate = args[cursor];
            if (candidate == "--")
            {
                cursor++;
                if (cursor >= args.Count)
                {
                    throw new UsageError($"a value is required for '{spec.Long}' but none was supplied");
                }

                taken.Add(spec.Parse(args[cursor]));
                cursor++;
                continue;
            }

            if (IsOptionToken(candidate))
            {
                throw new UsageError($"a value is required for '{spec.Long}' but none was supplied");
            }

            if (!spec.AllowNegative && LooksLikeNegativeNumber(candidate))
            {
                throw new UsageError($"unexpected argument '{candidate}'");
            }

            taken.Add(spec.Parse(candidate));
            cursor++;
        }

        if (spec.Arity.Kind == OptionArityKind.Exactly)
        {
            apply(spec, taken);
        }
        else
        {
            NoteSeen(seen, spec);
            apply(spec, taken[0]);
        }

        return cursor;
    }

    private static void NoteSeen(HashSet<string> seen, OptionSpec spec)
    {
        if (spec.Arity.Kind is OptionArityKind.Flag or OptionArityKind.One)
        {
            if (!seen.Add(spec.Long))
            {
                throw new UsageError($"the argument '{spec.Long}' cannot be used multiple times");
            }
        }
    }

    private static bool LooksLikeNegativeNumber(string token)
    {
        return token.Length >= 2 && token[0] == '-' && token[1] >= '0' && token[1] <= '9';
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static Dictionary<string, OptionSpec> IndexByLong(OptionSpec[] specs)
    {
        var map = new Dictionary<string, OptionSpec>(StringComparer.Ordinal);
        foreach (OptionSpec spec in specs)
        {
            map[spec.Long] = spec;
        }

        return map;
    }

    private static Dictionary<char, OptionSpec> IndexByShort(OptionSpec[] specs)
    {
        var map = new Dictionary<char, OptionSpec>();
        foreach (OptionSpec spec in specs)
        {
            if (spec.Short is char c)
            {
                map[c] = spec;
            }
        }

        return map;
    }

    private static void AssignRoot(RootOptions root, OptionSpec spec, object value)
    {
        switch (spec.Long)
        {
            case "--version":
                root.Version = true;
                break;
            case "--input-file":
                root.InputFile = (string)value;
                break;
            case "--tab-width":
                root.TabWidth = (long)value;
                break;
            case "--xterm-colors":
                root.XtermColors = true;
                break;
            case "--no-color":
                root.NoColor = true;
                break;
            case "--terminal-background-color":
                root.TerminalBackgroundColor = (Color)value;
                break;
            case "--existing-color-handling":
                root.ExistingColorHandling = (ExistingColorHandling)value;
                break;
            case "--wrap-text":
                root.WrapText = true;
                break;
            case "--frame-rate":
                root.FrameRate = (long)value;
                break;
            case "--canvas-width":
                root.CanvasWidth = (long)value;
                break;
            case "--canvas-height":
                root.CanvasHeight = (long)value;
                break;
            case "--anchor-canvas":
                root.AnchorCanvas = (Anchor)value;
                break;
            case "--anchor-text":
                root.AnchorText = (Anchor)value;
                break;
            case "--ignore-terminal-dimensions":
                root.IgnoreTerminalDimensions = true;
                break;
            case "--reuse-canvas":
                root.ReuseCanvas = true;
                break;
            case "--no-eol":
                root.NoEol = true;
                break;
            case "--no-restore-cursor":
                root.NoRestoreCursor = true;
                break;
            case "--seed":
                root.Seed = (ulong)value;
                break;
            case "--print-completion":
                root.PrintCompletion = (string)value;
                break;
            case "--random-effect":
                root.RandomEffect = true;
                break;
            case "--include-effects":
                AppendStrings(root.IncludeEffects, value);
                break;
            case "--exclude-effects":
                AppendStrings(root.ExcludeEffects, value);
                break;
            case "--m0-dump":
                root.M0Dump = true;
                break;
            case "--parity-dump":
                root.ParityDump = true;
                break;
            case "--max-frames":
                root.MaxFrames = (ulong)value;
                break;
            case "--virtual-clock":
                root.VirtualClock = true;
                break;
            case "--probe":
                root.Probe = true;
                break;
            case "--easing-golden-dump":
                root.EasingGoldenDump = true;
                break;
            case "--geometry-golden-dump":
                root.GeometryGoldenDump = true;
                break;
            default:
                throw new UsageError($"unexpected argument '{spec.Long}'");
        }
    }

    private static void AppendStrings(List<string> dest, object value)
    {
        if (value is List<object> list)
        {
            foreach (object item in list)
            {
                dest.Add((string)item);
            }

            return;
        }

        dest.Add((string)value);
    }
}
