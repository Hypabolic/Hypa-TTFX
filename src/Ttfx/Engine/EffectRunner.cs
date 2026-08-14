using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ttfx.Engine;

/// <summary>
/// One effect: build() once (upstream iterator __init__/build), then
/// next_frame() until None (upstream __next__/StopIteration).
/// Transcribed from <c>engine/effect.rs</c>.
/// </summary>
public enum RunOutcome
{
    Complete,
    Interrupted,
    Terminated,
    TerminalResized,
}

/// <summary>
/// Effect trait run loop (base_effect.py equivalents).
/// Transcribed from <c>engine/effect.rs</c>.
/// </summary>
public static class EffectRunner
{
    /// <summary>
    /// Parity mode: write length-prefixed frames to stdout, no tty escapes.
    /// <c>--max-frames 0</c> still emits one frame (emit before checking the limit).
    /// </summary>
    public static ulong DumpEffect(IEffect effect, EngineWorld world, ulong? maxFrames)
    {
        using Stream stdout = StdIo.OpenStdout();
        return DumpEffect(effect, world, maxFrames, stdout, Console.Error);
    }

    internal static ulong DumpEffect(
        IEffect effect,
        EngineWorld world,
        ulong? maxFrames,
        Stream stdout,
        TextWriter stderr)
    {
        effect.Build(world);
        ulong count = 0;
        while (true)
        {
            string? frame = effect.NextFrame(world);
            if (frame is null)
            {
                break;
            }

            byte[] data = Encoding.UTF8.GetBytes(frame);
            byte[] lengthLine = Encoding.UTF8.GetBytes(
                data.Length.ToString(CultureInfo.InvariantCulture) + "\n");
            stdout.Write(lengthLine);
            stdout.Write(data);
            stdout.Write("\n"u8);
            count += 1;
            if (maxFrames is ulong limit && count >= limit)
            {
                break;
            }
        }

        stdout.Flush();
        stderr.Write("frames=");
        stderr.Write(count.ToString(CultureInfo.InvariantCulture));
        stderr.Write('\n');
        return count;
    }

    /// <summary>
    /// __main__ run loop with terminal_output(): prep canvas, stream frames,
    /// always restore the cursor (even on error — RAII would not run on a raw
    /// process exit, so this is explicit).
    ///
    /// With <paramref name="stopOnResize"/>, a settled terminal resize also ends
    /// the pass, wiped and parked at the top of the area so the caller can
    /// rebuild in place.
    /// </summary>
    public static RunOutcome RunEffect(IEffect effect, EngineWorld world, bool stopOnResize = false)
    {
        using Stream stdout = StdIo.OpenStdout();
        return RunEffect(effect, world, stdout, stopOnResize);
    }

    internal static RunOutcome RunEffect(
        IEffect effect,
        EngineWorld world,
        Stream stdout,
        bool stopOnResize = false)
    {
        effect.Build(world);
        world.Terminal.PrepCanvas(stdout);
        RunOutcome outcome = RunOutcome.Complete;
        try
        {
            while (true)
            {
                if (RequestedStop(world, stopOnResize) is RunOutcome stop)
                {
                    outcome = stop;
                    break;
                }

                string? frame = effect.NextFrame(world);
                if (frame is null)
                {
                    break;
                }

                if (RequestedStop(world, stopOnResize) is RunOutcome stopAfter)
                {
                    outcome = stopAfter;
                    break;
                }

                world.Terminal.PrintFrame(stdout, frame);
            }
        }
        finally
        {
            if (outcome == RunOutcome.TerminalResized)
            {
                // Leave the cursor hidden and parked at the top of the wiped area: the
                // rebuild redraws in place, and showing the cursor here would strobe it
                // dozens of times a second through a window drag.
                world.Terminal.ResetCanvasArea(stdout);
            }
            else
            {
                world.Terminal.RestoreCursor(stdout, "\n");
            }

            try
            {
                stdout.Flush();
            }
            catch (BrokenPipeException)
            {
            }
        }

        return outcome;
    }

    private static RunOutcome? RequestedStop(EngineWorld world, bool stopOnResize)
    {
        if (Signals.Interrupted())
        {
            return RunOutcome.Interrupted;
        }

        if (Signals.Terminated())
        {
            return RunOutcome.Terminated;
        }

        if (stopOnResize && world.Terminal.ResizeSettled())
        {
            return RunOutcome.TerminalResized;
        }

        return null;
    }
}
