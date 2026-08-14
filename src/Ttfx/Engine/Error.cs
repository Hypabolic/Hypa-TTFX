using System;

namespace Ttfx.Engine;

/// <summary>
/// Engine error taxonomy. Error <em>conditions</em> match upstream; message text may differ.
/// Transcribed from <c>engine/error.rs</c>.
/// </summary>
public class EngineException : Exception
{
    public EngineException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Invariant failure. <c>Program.Main</c> does not catch this (plan §5.6).
/// </summary>
public sealed class EngineInvariantException : Exception
{
    public EngineInvariantException(string message)
        : base(message)
    {
    }
}
