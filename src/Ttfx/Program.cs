using System;
using System.IO;
using System.Reflection;
using System.Text;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;

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

        if (root.PrintCompletion is not null)
        {
            return 0;
        }

        if (root.Probe)
        {
            while (true)
            {
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

        if (root.RandomEffect)
        {
            if (CountFilteredEffects(root) == 0)
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

    private static int CountFilteredEffects(RootOptions root)
    {
        int available = 0;
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

            available++;
        }

        return available;
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
