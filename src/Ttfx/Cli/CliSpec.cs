using System;

namespace Ttfx.Cli;

public sealed class UsageError : Exception
{
    public UsageError(string message)
        : base(message)
    {
    }
}

public enum OptionArityKind
{
    Flag,
    One,
    AtLeastOne,
    Exactly,
}

public readonly struct OptionArity : IEquatable<OptionArity>
{
    public OptionArityKind Kind { get; }
    public int Count { get; }

    private OptionArity(OptionArityKind kind, int count)
    {
        Kind = kind;
        Count = count;
    }

    public static OptionArity Flag { get; } = new OptionArity(OptionArityKind.Flag, 0);
    public static OptionArity One { get; } = new OptionArity(OptionArityKind.One, 1);
    public static OptionArity AtLeastOne { get; } = new OptionArity(OptionArityKind.AtLeastOne, 1);

    public static OptionArity Exactly(int n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n));
        }

        return new OptionArity(OptionArityKind.Exactly, n);
    }

    public bool Equals(OptionArity other) => Kind == other.Kind && Count == other.Count;

    public override bool Equals(object? obj) => obj is OptionArity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Count);
}

public sealed record OptionSpec(
    string Long,
    char? Short,
    string MetaVar,
    string Help,
    OptionArity Arity,
    string? Default,
    Func<string, object> Parse,
    bool AllowNegative = false,
    string[]? DefaultValues = null);
