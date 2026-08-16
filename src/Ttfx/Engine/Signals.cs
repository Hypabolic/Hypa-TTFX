using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ttfx.Engine;

/// <summary>
/// POSIX signal flags and registrations. Handlers run on a thread-pool thread;
/// flags are Interlocked so the run loop cannot miss a signal under optimized
/// AOT. Registrations are held in statics for process lifetime.
/// Transcribed from <c>lib.rs</c>.
/// </summary>
public static partial class Signals
{
    private const int Sigterm = 15;
    private const nint SigDfl = 0;

    private static int _interrupted;
    private static int _terminated;
    private static int _resized;

    private static PosixSignalRegistration? _sigint;
    private static PosixSignalRegistration? _sigterm;
    private static PosixSignalRegistration? _sigwinch;

    /// <summary>
    /// SIGINT is recorded and checked from the run loop so teardown (cursor
    /// restore) happens through normal control flow — Drop alone would not run on
    /// a raw signal exit.
    /// </summary>
    public static void InstallSigintHandler()
    {
        if (_sigint is not null)
        {
            throw new EngineInvariantException("SIGINT already registered");
        }

        _sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, static context =>
        {
            context.Cancel = true;
            Interlocked.Exchange(ref _interrupted, 1);
        });
    }

    /// <summary>
    /// SIGTERM is recorded like SIGINT so a supervisor killing an animation gets
    /// the normal teardown instead of a hidden cursor. <see cref="DieFromSigterm"/>
    /// then finishes the job the handler deferred.
    /// </summary>
    public static void InstallSigtermHandler()
    {
        if (_sigterm is not null)
        {
            throw new EngineInvariantException("SIGTERM already registered");
        }

        _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, static context =>
        {
            context.Cancel = true;
            Interlocked.Exchange(ref _terminated, 1);
        });
    }

    /// <summary>
    /// Record terminal resizes so the CLI can rebuild effects whose canvas and
    /// character positions were derived from the previous dimensions.
    /// </summary>
    public static void InstallSigwinchHandler()
    {
        if (_sigwinch is not null)
        {
            throw new EngineInvariantException("SIGWINCH already registered");
        }

        // CA1416: PosixSignal.SIGWINCH is annotated Windows-unsupported; this
        // project is POSIX-only. The runtime guard stays.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
#pragma warning disable CA1416
            _sigwinch = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, static context =>
            {
                context.Cancel = true;
                Interlocked.Exchange(ref _resized, 1);
            });
#pragma warning restore CA1416
            return;
        }

        throw new PlatformNotSupportedException("SIGWINCH is POSIX-only");
    }

    public static bool Interrupted() => Interlocked.CompareExchange(ref _interrupted, 0, 0) != 0;

    public static bool Terminated() => Interlocked.CompareExchange(ref _terminated, 0, 0) != 0;

    /// <summary>Consume a pending terminal resize notification.</summary>
    public static bool TakeTerminalResize() => Interlocked.Exchange(ref _resized, 0) != 0;

    /// <summary>
    /// Undo the runtime's SIGTERM intercept so a redirected run dies from the
    /// signal the way a process that never installed a handler would.
    /// </summary>
    public static void RestoreDefaultSigterm()
    {
        _ = Signal(Sigterm, SigDfl);
    }

    /// <summary>
    /// Finish the SIGTERM we deferred: the cursor is back, so hand the signal to
    /// the default action and die from it. A supervisor then sees a terminated
    /// child, exactly as it would from the redirected run that never installs a
    /// handler at all. SIGINT does not go through here — upstream exits 1 on
    /// KeyboardInterrupt and parity outranks the convention.
    /// </summary>
    [DoesNotReturn]
    public static void DieFromSigterm()
    {
        PosixSignalRegistration? registration = Interlocked.Exchange(ref _sigterm, null);
        registration?.Dispose();
        _ = Signal(Sigterm, SigDfl);
        _ = Raise(Sigterm);
        throw new EngineInvariantException("SIGTERM with the default action terminates the process");
    }

    internal static void ForceResized() => Interlocked.Exchange(ref _resized, 1);

    internal static void ClearFlags()
    {
        Interlocked.Exchange(ref _interrupted, 0);
        Interlocked.Exchange(ref _terminated, 0);
        Interlocked.Exchange(ref _resized, 0);
    }

    [LibraryImport("libc", EntryPoint = "signal")]
    private static partial nint Signal(int signum, nint handler);

    [LibraryImport("libc", EntryPoint = "raise")]
    private static partial int Raise(int signum);
}
