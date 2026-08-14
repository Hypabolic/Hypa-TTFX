using System;

namespace Ttfx.Engine;

public sealed class UnsupportedAnsiException : Exception
{
    public string Sequence { get; }

    public UnsupportedAnsiException(string sequence)
        : base("unsupported ansi")
    {
        Sequence = sequence;
    }
}

/// <summary>
/// 0003 stub: reject known-unsupported CSI so stream routing is testable.
/// Full input parse is issue 0004.
/// </summary>
public static class InputParser
{
    public static void RejectUnsupported(string input)
    {
        int i = 0;
        while (i < input.Length)
        {
            if (input[i] == '\u001b' && i + 1 < input.Length && input[i + 1] == '[')
            {
                int j = i + 2;
                while (j < input.Length)
                {
                    char c = input[j];
                    if ((c >= '0' && c <= '9') || c == ';' || c == '?')
                    {
                        j++;
                        continue;
                    }

                    break;
                }

                if (j >= input.Length || input[j] != 'm')
                {
                    int len = (j < input.Length ? j + 1 : j) - i;
                    throw new UnsupportedAnsiException(input.Substring(i, len));
                }

                i = j + 1;
                continue;
            }

            i++;
        }
    }
}
