using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ttfx.Effects;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

/// <summary>
/// Runner contracts the frame-parity suite does not name: <c>--max-frames 0</c>
/// still emits one frame, the virtual clock's dt, cursor restore, and DEC
/// save/restore bytes (ESC 7 / ESC 8, not CSI s/u).
/// </summary>
internal static class RunnerSemanticsTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("virtual clock frame-rate 0 uses dt=1/60", VirtualClockDt);
        yield return new TestCase("clock advances inside Frame not the run loop", ClockAdvancesInsideFrame);
        yield return new TestCase("max-frames 0 emits one frame", MaxFramesZeroEmitsOne);
        yield return new TestCase("max-frames past completion emits all", MaxFramesPastCompletionEmitsAll);
        yield return new TestCase("run_effect restores cursor on error", RestoreCursorOnError);
        yield return new TestCase("DEC save/restore are ESC plus digit", DecSaveRestoreBytes);
        yield return new TestCase("tty run ends quietly when the terminal closes (EIO)", TtyOutputClosedEio);
        yield return new TestCase("tty run ends quietly when the pipe closes (EPIPE)", TtyOutputClosedEpipe);
        yield return new TestCase("redirected run still fails on EIO", RedirectedEioStillFails);
    }

    private static void VirtualClockDt()
    {
        var zero = (Clock.Virtual)Clock.VirtualWithFrameRate(0);
        Harness.AssertEqual("dt at 0", 1.0 / 60.0, zero.Dt);
        var negative = (Clock.Virtual)Clock.VirtualWithFrameRate(-1);
        Harness.AssertEqual("dt at -1", 1.0 / 60.0, negative.Dt);
        var sixty = (Clock.Virtual)Clock.VirtualWithFrameRate(60);
        Harness.AssertEqual("dt at 60", 1.0 / 60.0, sixty.Dt);
        var thirty = (Clock.Virtual)Clock.VirtualWithFrameRate(30);
        Harness.AssertEqual("dt at 30", 1.0 / 30.0, thirty.Dt);
    }

    private static void ClockAdvancesInsideFrame()
    {
        EngineWorld world = MakeWorld();
        var virt = (Clock.Virtual)world.Clock;
        Harness.AssertEqual("start", 0.0, virt.Now);
        var effect = new DoubleFrameEffect();
        effect.Build(world);
        string? frame = effect.NextFrame(world);
        Harness.AssertTrue("returned second frame", frame is not null);
        Harness.AssertEqual("advanced 2*dt", 2.0 * virt.Dt, virt.Now);
    }

    private static void MaxFramesZeroEmitsOne()
    {
        ulong count = DumpCounting(0, out string stderr);
        Harness.AssertEqual("count", 1UL, count);
        Harness.AssertTrue("stderr", stderr.Contains("frames=1", StringComparison.Ordinal));
    }

    private static void MaxFramesPastCompletionEmitsAll()
    {
        ulong count = DumpCounting(100, out string stderr);
        Harness.AssertEqual("count", 3UL, count);
        Harness.AssertTrue("stderr", stderr.Contains("frames=3", StringComparison.Ordinal));
    }

    private static void RestoreCursorOnError()
    {
        EngineWorld world = MakeWorld();
        using var stdout = new MemoryStream();
        try
        {
            EffectRunner.RunEffect(new ThrowingEffect(), world, stdout);
            Harness.AssertTrue("should throw", false);
        }
        catch (EngineException)
        {
            string text = Encoding.UTF8.GetString(stdout.ToArray());
            Harness.AssertTrue("hid cursor", text.Contains(Ansi.HideCursor, StringComparison.Ordinal));
            Harness.AssertTrue("showed cursor", text.Contains(Ansi.ShowCursor, StringComparison.Ordinal));
            Harness.AssertTrue("eol", text.EndsWith('\n'));
        }
    }

    private static void TtyOutputClosedEio()
    {
        var stdout = new ClosingStream(() => new OutputWriteException(StdIo.Eio));
        RunOutcome outcome = EffectRunner.RunEffect(new ClosingEffect(stdout), MakeWorld(), stdout, ttyOutput: true);
        Harness.AssertEqual("outcome", RunOutcome.OutputClosed, outcome);
        Harness.AssertEqual("no teardown writes after the close", 1, stdout.WritesAfterClose);
    }

    private static void TtyOutputClosedEpipe()
    {
        var stdout = new ClosingStream(() => new BrokenPipeException());
        RunOutcome outcome = EffectRunner.RunEffect(new ClosingEffect(stdout), MakeWorld(), stdout, ttyOutput: true);
        Harness.AssertEqual("outcome", RunOutcome.OutputClosed, outcome);
        Harness.AssertEqual("no teardown writes after the close", 1, stdout.WritesAfterClose);
    }

    private static void RedirectedEioStillFails()
    {
        var stdout = new ClosingStream(() => new OutputWriteException(StdIo.Eio));
        Harness.AssertThrows<OutputWriteException>(
            "EIO to a redirected stream is a real failure",
            () => EffectRunner.RunEffect(new ClosingEffect(stdout), MakeWorld(), stdout, ttyOutput: false));
    }

    private static void DecSaveRestoreBytes()
    {
        Harness.AssertEqual("save", "\u001b7", Ansi.DecSaveCursor);
        Harness.AssertEqual("restore", "\u001b8", Ansi.DecRestoreCursor);
        Harness.AssertEqual("save byte0", 0x1b, (int)Ansi.DecSaveCursor[0]);
        Harness.AssertEqual("save byte1", (int)'7', (int)Ansi.DecSaveCursor[1]);
    }

    private static ulong DumpCounting(ulong? maxFrames, out string stderr)
    {
        EngineWorld world = MakeWorld();
        using var stdout = new MemoryStream();
        var err = new StringWriter();
        (ulong count, _) = EffectRunner.DumpEffect(new ThreeFrameEffect(), world, maxFrames, stdout, err);
        stderr = err.ToString();
        return count;
    }

    private static EngineWorld MakeWorld()
    {
        var config = new TerminalConfig
        {
            CanvasWidth = 20,
            CanvasHeight = 8,
            IgnoreTerminalDimensions = true,
            FrameRate = 0,
        };
        return EngineWorld.New("hi", config, Rng.Seeded(1), Clock.VirtualWithFrameRate(0));
    }

    private sealed class DoubleFrameEffect : IEffect
    {
        private int _calls;

        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world)
        {
            if (_calls > 0)
            {
                return null;
            }

            _calls += 1;
            world.Frame();
            return world.Frame();
        }

        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
        }
    }

    private sealed class ThreeFrameEffect : IEffect
    {
        private int _n;

        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world)
        {
            if (_n >= 3)
            {
                return null;
            }

            _n += 1;
            return "f";
        }

        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
        }
    }

    /// <summary>Accepts writes until <see cref="Closed"/>, then fails every one.</summary>
    private sealed class ClosingStream : Stream
    {
        private readonly Func<IOException> _failure;

        public ClosingStream(Func<IOException> failure)
        {
            _failure = failure;
        }

        public bool Closed { get; set; }

        public int WritesAfterClose { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Closed)
            {
                WritesAfterClose += 1;
                throw _failure();
            }
        }
    }

    /// <summary>Emits frames forever; closes the output before its second frame.</summary>
    private sealed class ClosingEffect : IEffect
    {
        private readonly ClosingStream _stdout;
        private int _n;

        public ClosingEffect(ClosingStream stdout)
        {
            _stdout = stdout;
        }

        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world)
        {
            _n += 1;
            if (_n == 2)
            {
                _stdout.Closed = true;
            }

            return "f";
        }

        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
        }
    }

    private sealed class ThrowingEffect : IEffect
    {
        public void Build(EngineWorld world)
        {
        }

        public string? NextFrame(EngineWorld world) => throw new EngineException("boom");

        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
        }
    }
}
