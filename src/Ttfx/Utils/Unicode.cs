using System;
using System.Globalization;
using System.Text;
using Ttfx.Engine;

namespace Ttfx.Utils;

/// <summary>
/// Unicode helpers matching Rust <c>char</c> / <c>str::chars()</c> semantics.
/// One scalar value is one cell; never iterate UTF-16 <c>char</c>.
/// </summary>
public static class Unicode
{
    /// <summary>
    /// Rust <c>str::chars().count()</c>: number of Unicode scalar values.
    /// Not <c>string.Length</c> (UTF-16 code units).
    /// </summary>
    public static int RuneCount(string s)
    {
        int n = 0;
        foreach (Rune _ in s.EnumerateRunes())
        {
            n += 1;
        }

        return n;
    }

    /// <summary>
    /// binarypath.rs:159 — <c>format!("{code_point:08b}")</c> on
    /// <c>symbol.chars().next() as u32</c>. Uses the full scalar
    /// (<c>Rune.Value</c>), not a UTF-16 high surrogate.
    /// </summary>
    public static string CodepointToBinary(Rune rune)
    {
        return ((uint)rune.Value).ToString("B8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// binarypath.rs:159 wrapper: first scalar of <paramref name="symbol"/>,
    /// then <c>{:08b}</c>. Empty symbol is an invariant failure.
    /// </summary>
    public static string SymbolToBinary(string symbol)
    {
        var enumerator = symbol.EnumerateRunes();
        if (!enumerator.MoveNext())
        {
            throw new EngineInvariantException("empty symbol");
        }

        return CodepointToBinary(enumerator.Current);
    }

    /// <summary>
    /// Rust <c>char::to_digit</c>. ASCII <c>0-9</c> / <c>a-z</c> / <c>A-Z</c>
    /// only; Unicode digit forms (e.g. ３) return null. Not
    /// <c>char.GetNumericValue</c>. Radix outside 2..=36 is an invariant
    /// failure (Rust <c>assert!</c>).
    /// </summary>
    public static uint? ToDigit(Rune c, uint radix)
    {
        if (radix < 2 || radix > 36)
        {
            throw new EngineInvariantException("to_digit: radix must be in 2..=36");
        }

        int v = c.Value;
        uint val;
        if (v >= '0' && v <= '9')
        {
            val = (uint)(v - '0');
        }
        else if (v >= 'a' && v <= 'z')
        {
            val = (uint)(v - 'a' + 10);
        }
        else if (v >= 'A' && v <= 'Z')
        {
            val = (uint)(v - 'A' + 10);
        }
        else
        {
            return null;
        }

        if (val < radix)
        {
            return val;
        }

        return null;
    }

    /// <summary>Rust <c>c.to_digit(10)</c> — ASCII digits only.</summary>
    public static uint? ToDigit10(Rune c) => ToDigit(c, 10);

    /// <summary>
    /// swarm.rs:113 — <c>s.chars().next().and_then(|c| c.to_digit(10))</c>.
    /// </summary>
    public static long FirstCharDigit(string s)
    {
        var enumerator = s.EnumerateRunes();
        if (!enumerator.MoveNext())
        {
            throw new EngineInvariantException("path id must start with a digit");
        }

        uint? digit = ToDigit(enumerator.Current, 10);
        if (digit is null)
        {
            throw new EngineInvariantException("path id must start with a digit");
        }

        return digit.Value;
    }
}
