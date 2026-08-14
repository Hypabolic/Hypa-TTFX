using System;

namespace Ttfx.Utils;

/// <summary>
/// Minimal Color for CLI parsing. Equality is on the original argument
/// (plan §5.10). hex_to_xterm is issue 0005.
/// </summary>
public sealed class Color : IEquatable<Color>
{
    public string Original { get; }

    public Color(string original)
    {
        Original = original;
    }

    public bool Equals(Color? other) => other is not null && Original == other.Original;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => Original.GetHashCode(StringComparison.Ordinal);
}
