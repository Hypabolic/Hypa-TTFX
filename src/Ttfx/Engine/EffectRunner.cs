using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ttfx.Engine;

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
        using Stream stdout = Console.OpenStandardOutput();
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
    /// always restore the cursor (even on error). SIGINT/SIGTERM/SIGWINCH and
    /// stop_on_resize are issue 0012 — unused here.
    /// </summary>
    public static void RunEffect(IEffect effect, EngineWorld world)
    {
        using Stream stdout = Console.OpenStandardOutput();
        RunEffect(effect, world, stdout);
    }

    internal static void RunEffect(IEffect effect, EngineWorld world, Stream stdout)
    {
        effect.Build(world);
        world.Terminal.PrepCanvas(stdout);
        try
        {
            while (true)
            {
                string? frame = effect.NextFrame(world);
                if (frame is null)
                {
                    break;
                }

                world.Terminal.PrintFrame(stdout, frame);
            }
        }
        finally
        {
            world.Terminal.RestoreCursor(stdout, "\n");
            stdout.Flush();
        }
    }
}
