using System;
using System.Runtime.InteropServices;

namespace Ttfx.Engine;

/// <summary>
/// winsize + TIOCGWINSZ, verified against a C probe on this Darwin host.
///
/// Darwin (executed 2026-08-14): TIOCGWINSZ=0x40087468, sizeof(winsize)=8,
/// field offsets ws_row=0 ws_col=2 ws_xpixel=4 ws_ypixel=6.
///
/// Linux (from platform headers, not executed here): TIOCGWINSZ=0x5413
/// (asm-generic/ioctls.h), sizeof(struct winsize)=8 (four unsigned shorts,
/// bits/ioctl-types.h).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WinSize
{
    public ushort ws_row;
    public ushort ws_col;
    public ushort ws_xpixel;
    public ushort ws_ypixel;
}

public static partial class PosixTerminal
{
    // Darwin: _IOR('t', 104, struct winsize) = 0x40087468 (C probe).
    public const uint DarwinTiocgwinsz = 0x40087468;

    // Linux: TIOCGWINSZ in asm-generic/ioctls.h. Not executed on this Mac.
    public const uint LinuxTiocgwinsz = 0x5413;

    public const int WinSizeBytes = 8;

    public static nuint Tiocgwinsz
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return DarwinTiocgwinsz;
            }

            if (OperatingSystem.IsLinux())
            {
                return LinuxTiocgwinsz;
            }

            throw new PlatformNotSupportedException("TIOCGWINSZ is only defined for macOS and Linux");
        }
    }

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int fd, nuint request, out WinSize winsize);

    [LibraryImport("libc", EntryPoint = "isatty", SetLastError = true)]
    internal static partial int IsAtty(int fd);

    /// <summary>
    /// tty-lifecycle gate: stdout only. Do not conflate with the
    /// stdout→stderr→stdin size cascade.
    /// </summary>
    public static bool IsStdoutTty() => IsAtty(1) == 1;

    /// <summary>
    /// terminal_size crate: stdout, then stderr, then stdin; first that is a
    /// tty <em>and</em> reports positive rows and columns.
    /// </summary>
    public static (long Width, long Height)? QueryTtySize()
    {
        ReadOnlySpan<int> fds = [1, 2, 0];
        foreach (int fd in fds)
        {
            if (IsAtty(fd) != 1)
            {
                continue;
            }

            if (Ioctl(fd, Tiocgwinsz, out WinSize ws) != 0)
            {
                continue;
            }

            if (ws.ws_row > 0 && ws.ws_col > 0)
            {
                return (ws.ws_col, ws.ws_row);
            }
        }

        return null;
    }
}
