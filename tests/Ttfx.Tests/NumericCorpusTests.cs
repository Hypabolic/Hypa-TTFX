using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ttfx.Cli;

namespace Ttfx.Tests;

internal static class NumericCorpusTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("numeric corpus agreement", RunCorpus);
    }

    private static void RunCorpus()
    {
        string path = Path.Combine(Harness.FindRepoRoot(), "tests", "Ttfx.Tests", "fixtures", "numeric_corpus.txt");
        int lineNo = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            lineNo++;
            if (raw.Length == 0 || raw[0] == '#')
            {
                continue;
            }

            string[] cols = raw.Split('\t');
            if (cols[0] == "rust")
            {
                CheckRust(lineNo, cols);
            }
            else if (cols[0] == "cli")
            {
                CheckCli(lineNo, cols);
            }
            else
            {
                Harness.AssertTrue($"corpus L{lineNo} kind", false);
            }
        }
    }

    private static void CheckRust(int lineNo, string[] cols)
    {
        string kind = cols[1];
        string token = DecodeHex(cols[2]);
        bool accept = cols[3] == "accept";
        string name = $"rust {kind} L{lineNo} {FormatToken(token)}";

        if (kind == "i64")
        {
            bool ok = ValueParsers.TryParseI64(token, out long value);
            Harness.AssertEqual(name + " accept", accept, ok);
            if (ok && accept)
            {
                long expected = long.Parse(cols[4], CultureInfo.InvariantCulture);
                Harness.AssertEqual(name + " value", expected, value);
            }
        }
        else if (kind == "u64")
        {
            bool ok = ValueParsers.TryParseU64(token, out ulong value);
            Harness.AssertEqual(name + " accept", accept, ok);
            if (ok && accept)
            {
                ulong expected = ulong.Parse(cols[4], CultureInfo.InvariantCulture);
                Harness.AssertEqual(name + " value", expected, value);
            }
        }
        else if (kind == "f64")
        {
            bool ok = ValueParsers.TryParseF64(token, out double value);
            Harness.AssertEqual(name + " accept", accept, ok);
            if (ok && accept)
            {
                ulong expected = ulong.Parse(cols[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                Harness.AssertEqual(name + " bits", expected, BitConverter.DoubleToUInt64Bits(value));
            }
        }
        else
        {
            Harness.AssertTrue(name + " kind", false);
        }
    }

    private static void CheckCli(int lineNo, string[] cols)
    {
        string option = cols[1];
        string token = DecodeHex(cols[2]);
        bool accept = cols[3] == "accept";
        string name = $"cli --{option} L{lineNo} {FormatToken(token)}";
        bool ok = true;
        try
        {
            CliParser.Parse(["--" + option, token]);
        }
        catch (UsageError)
        {
            ok = false;
        }

        Harness.AssertEqual(name, accept, ok);
    }

    private static string DecodeHex(string hex)
    {
        if (hex.Length == 0)
        {
            return "";
        }

        byte[] bytes = Convert.FromHexString(hex);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static string FormatToken(string token)
    {
        if (token.Length == 0)
        {
            return "<empty>";
        }

        return token.Replace('\n', 'n').Replace('\t', 't');
    }
}
