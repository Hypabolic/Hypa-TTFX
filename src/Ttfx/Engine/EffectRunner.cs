using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ttfx.Engine;

/// <summary>
/// One effect: build() once (upstream iterator __init__/build), then
/// next_frame() until None (upstream __next__/StopIteration).
/// </summary>
public enum RunOutcome
{
    Complete,
    Interrupted,
    Terminated,
    TerminalResized,

    /// <summary>The terminal went away mid-run; there was nothing left to draw on.</summary>
    OutputClosed,
}

/// <summary>
/// Effect run loop (base_effect.py equivalents).
/// </summary>
public static class EffectRunner
{
    /// <summary>
    /// Parity mode: write length-prefixed frames to stdout, no tty escapes.
    /// <c>--max-frames 0</c> still emits one frame (emit before checking the limit).
    /// Returns frame count and whether the effect reported completion.
    /// </summary>
    public static (ulong Count, bool Complete) DumpEffect(IEffect effect, EngineWorld world, ulong? maxFrames)
    {
        using Stream stdout = StdIo.OpenStdout();
        return DumpEffect(effect, world, maxFrames, stdout, Console.Error);
    }

    internal static (ulong Count, bool Complete) DumpEffect(
        IEffect effect,
        EngineWorld world,
        ulong? maxFrames,
        Stream stdout,
        TextWriter stderr)
    {
        world.FrameTextDeferred = true;
        effect.Build(world);
        ulong count = 0;
        bool complete = false;
        Span<byte> lengthLine = stackalloc byte[24];
        while (true)
        {
            string? frame = effect.NextFrame(world);
            if (frame is null)
            {
                complete = true;
                break;
            }

            ReadOnlySpan<byte> data = world.FrameBytes(frame).Span;
            data.Length.TryFormat(lengthLine, out int digits, provider: CultureInfo.InvariantCulture);
            lengthLine[digits] = (byte)'\n';
            stdout.Write(lengthLine[..(digits + 1)]);
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
        return (count, complete);
    }

    /// <summary>
    /// __main__ run loop with terminal_output(): prep canvas, stream frames,
    /// always restore the cursor (even on error — RAII would not run on a raw
    /// process exit, so this is explicit).
    ///
    /// With <paramref name="ttyOutput"/>, a settled terminal resize also ends
    /// the pass, wiped and parked at the top of the area so the caller can
    /// rebuild in place, and a terminal that goes away ends the run
    /// (<see cref="RunOutcome.OutputClosed"/>). A redirected stream gets
    /// neither: SIGWINCH there is not about our output, and a write that fails
    /// to a file is a real failure.
    /// </summary>
    public static RunOutcome RunEffect(IEffect effect, EngineWorld world, bool ttyOutput = false)
    {
        using Stream stdout = StdIo.OpenStdout();
        return RunEffect(effect, world, stdout, ttyOutput);
    }

    internal static RunOutcome RunEffect(
        IEffect effect,
        EngineWorld world,
        Stream stdout,
        bool ttyOutput = false)
    {
        world.FrameTextDeferred = true;
        effect.Build(world);
        try
        {
            world.Terminal.PrepCanvas(stdout);
        }
        catch (IOException ex) when (ttyOutput && IsOutputClosed(ex))
        {
            return RunOutcome.OutputClosed;
        }

        RunOutcome outcome = RunOutcome.Complete;
        bool outputClosed = false;
        try
        {
            while (true)
            {
                if (RequestedStop(world, ttyOutput) is RunOutcome stop)
                {
                    outcome = stop;
                    break;
                }

                string? frame = effect.NextFrame(world);
                if (frame is null)
                {
                    break;
                }

                if (RequestedStop(world, ttyOutput) is RunOutcome stopAfter)
                {
                    outcome = stopAfter;
                    break;
                }

                world.Terminal.PrintFrame(stdout, world.FrameBytes(frame).Span);
            }
        }
        catch (IOException ex) when (ttyOutput && IsOutputClosed(ex))
        {
            outputClosed = true;
        }
        finally
        {
            // Every write after the terminal is gone fails, and there is no
            // cursor left to restore, so a closed output skips the teardown.
            if (!outputClosed)
            {
                try
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
                catch (IOException ex) when (ttyOutput && IsOutputClosed(ex))
                {
                    outputClosed = true;
                }
            }
        }

        return outputClosed ? RunOutcome.OutputClosed : outcome;
    }

    /// <summary>
    /// Whether a failed write means the terminal is gone: the screensaver's
    /// window was killed at lock, the emulator exited, a reader closed the
    /// pipe. EIO is the pty slave outliving its master; EPIPE is the reader
    /// of a pipe going away. Neither is a failure of the run, and there is
    /// no one left to report it to.
    /// </summary>
    internal static bool IsOutputClosed(IOException ex) =>
        ex is BrokenPipeException or OutputWriteException { Errno: StdIo.Eio };

    private static RunOutcome? RequestedStop(EngineWorld world, bool ttyOutput)
    {
        if (Signals.Interrupted())
        {
            return RunOutcome.Interrupted;
        }

        if (Signals.Terminated())
        {
            return RunOutcome.Terminated;
        }

        if (ttyOutput && world.Terminal.ResizeSettled())
        {
            return RunOutcome.TerminalResized;
        }

        return null;
    }
}
