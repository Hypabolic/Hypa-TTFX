using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ttfx.Engine;

/// <summary>
/// Broken-pipe exit is an accepted divergence: 0, not 141 (plan.md §8.2).
/// Writes go through write(2) so EPIPE (errno 32) is distinguished from
/// disk-full and other genuine write failures — Stream.Write's HResult is
/// not reliably errno after a managed write.
/// </summary>
public sealed class BrokenPipeException : IOException
{
    public BrokenPipeException()
        : base("Broken pipe")
    {
    }
}

/// <summary>Raw stdout via write(2). Transcribed intent from plan.md §8.2.</summary>
public static partial class StdIo
{
    public const int StdoutFd = 1;
    public const int Epipe = 32;

    public static Stream OpenStdout() => new PosixFdStream(StdoutFd);

    public static void Write(int fd, ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            nint written;
            unsafe
            {
                fixed (byte* ptr = data)
                {
                    written = WriteFd(fd, ptr, data.Length);
                }
            }

            if (written < 0)
            {
                int err = Marshal.GetLastPInvokeError();
                if (err == Epipe)
                {
                    throw new BrokenPipeException();
                }

                throw new IOException($"write failed: errno {err}");
            }

            if (written == 0)
            {
                throw new IOException("write returned 0");
            }

            data = data.Slice((int)written);
        }
    }

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static unsafe partial nint WriteFd(int fd, byte* buf, nint count);
}

/// <summary>Unbuffered Stream over a POSIX fd, using write(2).</summary>
internal sealed class PosixFdStream : Stream
{
    private readonly int _fd;

    public PosixFdStream(int fd)
    {
        _fd = fd;
    }

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

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        StdIo.Write(_fd, buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer) => StdIo.Write(_fd, buffer);
}
