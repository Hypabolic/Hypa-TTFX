using System;
using System.Diagnostics;

namespace Ttfx.Engine;

/// <summary>
/// Virtual/real clock. Matrix reads wall time, thunderstorm
/// reads monotonic time; the parity harness swaps in the virtual variant.
/// Transcribed from <c>engine/ctx.rs</c>.
/// </summary>
public abstract class Clock
{
    private Clock()
    {
    }

    public sealed class Real : Clock
    {
        public long StartTimestamp { get; }
        public double WallStart { get; }

        public Real(long startTimestamp, double wallStart)
        {
            StartTimestamp = startTimestamp;
            WallStart = wallStart;
        }
    }

    public sealed class Virtual : Clock
    {
        public double Now { get; set; }
        public double Dt { get; }

        public Virtual(double now, double dt)
        {
            Now = now;
            Dt = dt;
        }
    }

    public static Clock MakeReal()
    {
        // Capture epoch once as fractional seconds (ctx.rs:43-47); add monotonic
        // elapsed thereafter. Do not use ToUnixTimeMilliseconds().
        double wallStart = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        return new Real(Stopwatch.GetTimestamp(), wallStart);
    }

    public static Clock VirtualWithFrameRate(long frameRate)
    {
        double dt = frameRate > 0 ? 1.0 / frameRate : 1.0 / 60.0;
        return new Virtual(0.0, dt);
    }

    /// <summary>time.time() analog.</summary>
    public double NowWall()
    {
        switch (this)
        {
            case Real real:
                return real.WallStart + ElapsedSeconds(real.StartTimestamp);
            case Virtual virtualClock:
                return virtualClock.Now;
            default:
                throw new EngineInvariantException("unknown clock");
        }
    }

    /// <summary>time.monotonic() analog.</summary>
    public double NowMonotonic()
    {
        switch (this)
        {
            case Real real:
                return ElapsedSeconds(real.StartTimestamp);
            case Virtual virtualClock:
                return virtualClock.Now;
            default:
                throw new EngineInvariantException("unknown clock");
        }
    }

    /// <summary>Advance virtual time by one frame; no-op for the real clock.</summary>
    public void AdvanceFrame()
    {
        if (this is Virtual virtualClock)
        {
            virtualClock.Now += virtualClock.Dt;
        }
    }

    private static double ElapsedSeconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
    }
}
