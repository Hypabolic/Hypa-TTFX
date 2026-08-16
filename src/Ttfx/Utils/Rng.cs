using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ttfx.Engine;

namespace Ttfx.Utils;

/// <summary>
/// Engine RNG: xoshiro256++ with Python-<c>random</c>-shaped helpers.
///
/// The helper semantics here are the parity contract: tools/parity/shim.py
/// implements the exact same algorithms in Python and monkeypatches the
/// <c>random</c> module with them, so both implementations draw identical
/// sequences given the same seed. Do not change any helper's
/// algorithm without updating the shim in lockstep.
/// Transcribed from <c>utils/rng.rs</c>.
/// </summary>
public sealed class Rng
{
    private readonly ulong[] _s;

    private Rng(ulong[] s)
    {
        _s = s;
    }

    public static Rng Seeded(ulong seed)
    {
        // SplitMix64 expansion of the seed into the xoshiro state, the
        // reference-recommended initialization.
        ulong sm = seed;
        ulong Next()
        {
            sm = unchecked(sm + 0x9E3779B97F4A7C15UL);
            ulong z = sm;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        return new Rng([Next(), Next(), Next(), Next()]);
    }

    public static Rng FromEntropy()
    {
        Span<byte> buf = stackalloc byte[8];
        // Unseeded runs are not compared. RandomNumberGenerator.Fill is BCL,
        // AOT-clean, and stands in for Rust's /dev/urandom read.
        RandomNumberGenerator.Fill(buf);
        return Seeded(BinaryPrimitives.ReadUInt64LittleEndian(buf));
    }

    /// <summary>Core generator: xoshiro256++ next().</summary>
    private ulong NextU64()
    {
        ulong result = unchecked(BitOperations.RotateLeft(_s[0] + _s[3], 23) + _s[0]);
        ulong t = _s[1] << 17;
        _s[2] ^= _s[0];
        _s[3] ^= _s[1];
        _s[1] ^= _s[2];
        _s[0] ^= _s[3];
        _s[2] ^= t;
        _s[3] = BitOperations.RotateLeft(_s[3], 45);
        return result;
    }

    /// <summary>
    /// Python random.random() shape: float in [0, 1) with 53 bits of precision.
    /// </summary>
    public double Random()
    {
        return (double)(NextU64() >> 11) * (1.0 / (double)(1UL << 53));
    }

    /// <summary>
    /// Uniform integer in [0, n) via Lemire-free simple rejection on bit masks —
    /// deterministic and trivially portable to the Python shim.
    /// </summary>
    private ulong Randbelow(ulong n)
    {
        if (n == 0)
        {
            throw new EngineInvariantException("randbelow(0)");
        }

        int bits = 64 - BitOperations.LeadingZeroCount(n - 1);
        while (true)
        {
            ulong r = NextU64() >> (64 - Math.Max(bits, 1));
            if (r < n)
            {
                return r;
            }
        }
    }

    /// <summary>random.randint(a, b): inclusive both ends.</summary>
    public long Randint(long a, long b)
    {
        if (a > b)
        {
            throw new EngineInvariantException($"randint range empty: {a}..={b}");
        }

        return unchecked(a + (long)Randbelow(unchecked((ulong)(b - a + 1))));
    }

    /// <summary>random.randrange(a, b): half-open.</summary>
    public long Randrange(long a, long b)
    {
        if (a >= b)
        {
            throw new EngineInvariantException($"randrange range empty: {a}..{b}");
        }

        return unchecked(a + (long)Randbelow(unchecked((ulong)(b - a))));
    }

    /// <summary>random.choice(seq): seq[randbelow(len)].</summary>
    public T Choice<T>(IReadOnlyList<T> seq)
    {
        if (seq.Count == 0)
        {
            throw new EngineInvariantException("choice on empty sequence");
        }

        return seq[(int)Randbelow((ulong)seq.Count)];
    }

    /// <summary>random.choice by index, for callers that need an owned element.</summary>
    public int ChoiceIndex(int len)
    {
        if (len <= 0)
        {
            throw new EngineInvariantException("choice on empty sequence");
        }

        return (int)Randbelow((ulong)len);
    }

    /// <summary>random.uniform(a, b): a + (b-a) * random().</summary>
    public double Uniform(double a, double b)
    {
        return a + (b - a) * Random();
    }

    /// <summary>
    /// random.shuffle: Fisher-Yates from the top, exactly CPython's loop
    /// (for i in reversed(range(1, len(x))): j = randbelow(i+1); swap).
    /// </summary>
    public void Shuffle<T>(Span<T> seq)
    {
        for (int i = seq.Length - 1; i >= 1; i--)
        {
            int j = (int)Randbelow(unchecked((ulong)(i + 1)));
            (seq[i], seq[j]) = (seq[j], seq[i]);
        }
    }

    public void Shuffle<T>(T[] seq) => Shuffle(seq.AsSpan());

    public void Shuffle<T>(List<T> seq) => Shuffle(CollectionsMarshal.AsSpan(seq));
}
