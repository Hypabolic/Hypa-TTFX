using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx;

internal static class Program
{
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static int Main(string[] args)
    {
        ParseResult parsed;
        try
        {
            parsed = CliParser.Parse(args);
        }
        catch (UsageError)
        {
            return 2;
        }

        RootOptions root = parsed.Root;

        if (root.Version)
        {
            Version version = typeof(Program).Assembly.GetName().Version!;
            Console.WriteLine($"ttfx {version.Major}.{version.Minor}.{version.Build}");
            return 0;
        }

        // Hidden golden dumps: AOT-published binary, no input required.
        // Use write(2), not Console.OpenStandardOutput — osx-arm64 ILC
        // FailFast/AVs on the Console stream when stdout is a pipe.
        if (root.EasingGoldenDump)
        {
            using Stream stdout = StdIo.OpenStdout();
            return GoldenDumps.WriteEasing(stdout);
        }

        if (root.GeometryGoldenDump)
        {
            return GoldenDumps.WriteGeometry(Console.Out);
        }

        if (root.PrintCompletion is not null)
        {
            Completions.Print(root.PrintCompletion, Console.Out);
            return 0;
        }

        if (root.Probe)
        {
            if (!root.ParityDump)
            {
                Signals.InstallSigintHandler();
            }

            if (PosixTerminal.IsStdoutTty())
            {
                Signals.InstallSigtermHandler();
            }
            else
            {
                Signals.RestoreDefaultSigterm();
            }

            while (true)
            {
                if (Signals.Terminated())
                {
                    Signals.DieFromSigterm();
                }

                if (Signals.Interrupted())
                {
                    return 1;
                }

                Console.Out.Write('\n');
            }
        }

        string inputData;
        try
        {
            inputData = ReadInput(root.InputFile);
        }
        catch (DecoderFallbackException ex)
        {
            if (root.InputFile is not null)
            {
                Console.WriteLine($"Error reading input file: {ex.Message}");
            }
            else
            {
                Console.WriteLine($"Error decoding input: {ex.Message}");
            }

            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"Error reading input file: {ex.Message}");
            return 1;
        }

        if (inputData.Trim().Length == 0)
        {
            Console.WriteLine("NO INPUT.");
            return 1;
        }

        // main.rs: m0_dump runs after the empty-input check and before effect
        // resolution — --m0-dump does not require an effect.
        if (root.M0Dump)
        {
            try
            {
                return M0Dump(inputData, root);
            }
            catch (BrokenPipeException)
            {
                return 0;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        if (root.RandomEffect)
        {
            if (FilteredEffectNames(root).Count == 0)
            {
                Console.Error.WriteLine("Error: No effects available after filtering.");
                return 1;
            }
        }
        else if (parsed.EffectName is null)
        {
            Console.Error.WriteLine("Error: No effect specified.");
            return 1;
        }

        // After effect resolution, matching main.rs: EngineCtx::new is only
        // reached once an effect is known.
        try
        {
            InputParser.RejectUnsupported(inputData);
        }
        catch (UnsupportedAnsiException ex)
        {
            Console.Error.WriteLine($"Error: Unsupported ANSI sequence in input data: {ex.Sequence}");
            return 1;
        }
        catch (EngineException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        Rng rng = root.Seed is ulong seed ? Rng.Seeded(seed) : Rng.FromEntropy();

        EffectSpec spec;
        Dictionary<string, object> effectOptions;
        if (root.RandomEffect)
        {
            List<string> names = FilteredEffectNames(root);
            string name = names[rng.ChoiceIndex(names.Count)];
            ParseResult defaultsOnly = CliParser.Parse([name]);
            spec = EffectRegistry.Find(name)!;
            if (spec.Factory is null)
            {
                Console.Error.WriteLine($"Error: failed to build effect '{name}'.");
                return 1;
            }

            effectOptions = defaultsOnly.EffectOptions;
        }
        else
        {
            spec = EffectRegistry.Find(parsed.EffectName!)!;
            if (spec.Factory is null)
            {
                Console.Error.WriteLine($"Error: failed to build effect '{parsed.EffectName}'.");
                return 1;
            }

            effectOptions = parsed.EffectOptions;
        }

        // SIGWINCH is delivered to every process in the terminal's foreground group,
        // whatever its stdout points at. Reacting to it when the animation is being
        // redirected would leave a truncated first run followed by a complete second
        // one in the file. SIGTERM teardown is tty-only for the same reason: a
        // redirected stream must not gain teardown bytes. Only the teardown differs
        // — the tty run re-raises afterwards, so both die from the signal.
        bool ttyOutput = !root.ParityDump && PosixTerminal.IsStdoutTty();
        if (!root.ParityDump)
        {
            Signals.InstallSigintHandler();
        }

        if (ttyOutput)
        {
            Signals.InstallSigtermHandler();
            Signals.InstallSigwinchHandler();
        }
        else
        {
            Signals.RestoreDefaultSigterm();
        }

        TerminalConfig config = TerminalConfig.FromRoot(root);
        bool rebuildTriggered = false;
        ulong totalEmitted = 0;
        try
        {
            while (true)
            {
                Clock clock = root.ParityDump || root.VirtualClock
                    ? Clock.VirtualWithFrameRate(config.FrameRate)
                    : Clock.MakeReal();
                EngineWorld world = EngineWorld.New(inputData, config, rng, clock);
                IEffect effect = spec.Factory!(effectOptions);
                RunOutcome outcome;
                if (root.ParityDump)
                {
                    ulong? dumpLimit = null;
                    if (root.MaxFrames is ulong maxFrames)
                    {
                        dumpLimit = maxFrames - totalEmitted;
                    }

                    if (root.RebuildAfter is ulong rebuildAt && !rebuildTriggered)
                    {
                        ulong cap = rebuildAt;
                        if (dumpLimit is ulong limit && limit < cap)
                        {
                            cap = limit;
                        }

                        dumpLimit = cap;
                    }

                    (ulong emitted, bool complete) = EffectRunner.DumpEffect(effect, world, dumpLimit);
                    totalEmitted += emitted;
                    if (root.RebuildAfter is ulong rebuildAfter
                        && !rebuildTriggered
                        && emitted >= rebuildAfter
                        && !complete)
                    {
                        // No second pass when the max-frames budget is already spent —
                        // DumpEffect(0) still emits one frame by contract.
                        if (root.MaxFrames is not ulong maxBudget || totalEmitted < maxBudget)
                        {
                            config.ReuseCanvas = false;
                            rng = world.Rng;
                            rebuildTriggered = true;
                            continue;
                        }
                    }

                    outcome = RunOutcome.Complete;
                }
                else
                {
                    outcome = EffectRunner.RunEffect(effect, world, ttyOutput);
                }

                if (outcome == RunOutcome.TerminalResized)
                {
                    // run_effect wiped the old area and left the cursor at its top,
                    // so the rebuild lays out from here. --reuse-canvas would send
                    // prep_canvas to a DEC anchor that no longer applies, so it only
                    // governs the first run.
                    config.ReuseCanvas = false;
                    rng = world.Rng;
                    continue;
                }

                break;
            }
        }
        catch (BrokenPipeException)
        {
            return 0;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (UnsupportedAnsiException ex)
        {
            Console.Error.WriteLine($"Error: Unsupported ANSI sequence in input data: {ex.Sequence}");
            return 1;
        }
        catch (EngineException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        if (Signals.Terminated())
        {
            Signals.DieFromSigterm();
        }

        if (Signals.Interrupted())
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// M0 parity path: build the Terminal, make every character in
    /// character_by_input_coord visible, print the first frame to stdout.
    /// Transcribed from <c>main.rs</c> m0_dump (208-228).
    /// </summary>
    private static int M0Dump(string inputData, RootOptions root)
    {
        Terminal terminal;
        try
        {
            terminal = Terminal.New(inputData, TerminalConfig.FromRoot(root));
        }
        catch (UnsupportedAnsiException ex)
        {
            Console.Error.WriteLine($"Error: Unsupported ANSI sequence in input data: {ex.Sequence}");
            return 1;
        }
        catch (EngineException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        var ids = new System.Collections.Generic.List<CharId>(terminal.CharacterByInputCoord.Values);
        foreach (CharId id in ids)
        {
            terminal.SetCharacterVisibility(id, true);
        }

        ReadOnlyMemory<byte> frame = terminal.GetFormattedOutputString();
        using Stream stdout = StdIo.OpenStdout();
        stdout.Write(frame.Span);
        stdout.Write("\n"u8);
        return 0;
    }

    private static string ReadInput(string? inputFile)
    {
        if (inputFile is not null)
        {
            byte[] bytes = File.ReadAllBytes(inputFile);
            return StrictUtf8.GetString(bytes);
        }

        if (!Console.IsInputRedirected)
        {
            return "";
        }

        using Stream stdin = Console.OpenStandardInput();
        using var ms = new MemoryStream();
        stdin.CopyTo(ms);
        return StrictUtf8.GetString(ms.ToArray());
    }

    private static List<string> FilteredEffectNames(RootOptions root)
    {
        var names = new List<string>();
        foreach (EffectSpec spec in EffectRegistry.Effects)
        {
            if (root.IncludeEffects.Count > 0 && !ContainsOrdinal(root.IncludeEffects, spec.Name))
            {
                continue;
            }

            if (ContainsOrdinal(root.ExcludeEffects, spec.Name))
            {
                continue;
            }

            names.Add(spec.Name);
        }

        return names;
    }

    private static bool ContainsOrdinal(System.Collections.Generic.List<string> items, string name)
    {
        foreach (string item in items)
        {
            if (item == name)
            {
                return true;
            }
        }

        return false;
    }
}
