"""Terminal-close behavior, driven on a real pty.

A screensaver's terminal window can be destroyed while ttfx is mid-frame
(issue #1): the pty master closes, and every later write to the slave fails
with EIO. ttfx must treat that as the end of the run, not abort. The kernel's
SIGHUP normally wins that race, so the child ignores SIGHUP here to make the
write path deterministic.

Checks, each over a few timings:
- closing the terminal mid-animation exits 0, never SIGABRT;
- SIGTERM sent as the terminal closes ends by SIGTERM or exit 0, never SIGABRT.

Usage: terminal_close_behavior.py [path-to-ttfx]
"""

from __future__ import annotations

import fcntl
import os
import pty
import select
import signal
import struct
import sys
import termios
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BIN = sys.argv[1] if len(sys.argv) > 1 else str(ROOT / "artifacts/ttfx")
HIDE = b"\x1b[?25l"
# The screensaver command line from the issue, minus the input file.
ARGS = [
    "--frame-rate", "120", "--canvas-width", "0", "--canvas-height", "0",
    "--reuse-canvas", "--anchor-canvas", "c", "--anchor-text", "c",
    "--no-eol", "--no-restore-cursor",
]
EFFECTS = ["matrix", "wipe", "beams"]
DELAYS = [0.0, 0.2, 0.6]


def spawn(effect: str):
    sin_r, sin_w = os.pipe()
    pid, fd = pty.fork()
    if pid == 0:
        signal.signal(signal.SIGHUP, signal.SIG_IGN)
        os.close(sin_w)
        os.dup2(sin_r, 0)
        os.close(sin_r)
        env = {k: v for k, v in os.environ.items() if k not in ("COLUMNS", "LINES")}
        os.execve(BIN, [BIN] + ARGS + [effect], env)
        os._exit(127)
    os.close(sin_r)
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))
    os.write(sin_w, (ROOT / "hypa-logo.txt").read_bytes())
    os.close(sin_w)
    return pid, fd


def drain(fd: int, seconds: float, until: bytes | None = None) -> None:
    captured = bytearray()
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if until is not None and until in captured:
            return
        if not select.select([fd], [], [], 0.02)[0]:
            continue
        try:
            chunk = os.read(fd, 65536)
        except OSError:
            return
        if not chunk:
            return
        captured.extend(chunk)


def reap(pid: int) -> int:
    deadline = time.monotonic() + 5.0
    while time.monotonic() < deadline:
        done, status = os.waitpid(pid, os.WNOHANG)
        if done:
            return status
        time.sleep(0.01)
    os.kill(pid, signal.SIGKILL)
    return os.waitpid(pid, 0)[1]


def run(effect: str, delay: float, sigterm: bool) -> int:
    pid, fd = spawn(effect)
    drain(fd, 2.0, until=HIDE)
    drain(fd, delay)
    if sigterm:
        os.kill(pid, signal.SIGTERM)
    os.close(fd)
    return reap(pid)


def describe(status: int) -> str:
    if os.WIFSIGNALED(status):
        return signal.Signals(os.WTERMSIG(status)).name
    return f"exit {os.WEXITSTATUS(status)}"


def main() -> int:
    checks = []
    for effect in EFFECTS:
        for delay in DELAYS:
            status = run(effect, delay, sigterm=False)
            checks.append((f"{effect} closed at +{delay}s exits 0 ({describe(status)})",
                           os.WIFEXITED(status) and os.WEXITSTATUS(status) == 0))
            status = run(effect, delay, sigterm=True)
            clean = (os.WIFSIGNALED(status) and os.WTERMSIG(status) == signal.SIGTERM) or (
                os.WIFEXITED(status) and os.WEXITSTATUS(status) == 0)
            checks.append((f"{effect} SIGTERM + close at +{delay}s ends cleanly ({describe(status)})", clean))
    for label, passed in checks:
        print(f"  {'ok  ' if passed else 'FAIL'} {label}")
    failures = sum(not passed for _, passed in checks)
    print(f"\nterminal close behavior: {'all checks passed' if not failures else f'{failures} failed'}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
