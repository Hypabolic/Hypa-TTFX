"""SIGINT teardown behavior, driven on a real pty.

Ctrl-C must restore the cursor and exit 1 with no message. The run loop
unwinds so restore_cursor runs; the process is not killed by the signal.

Usage: sigint_behavior.py [path-to-ttfx]
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
HIDE, SHOW = b"\x1b[?25l", b"\x1b[?25h"
ARGS = ["--frame-rate", "30", "wipe"]


def spawn():
    sin_r, sin_w = os.pipe()
    pid, fd = pty.fork()
    if pid == 0:
        os.close(sin_w)
        os.dup2(sin_r, 0)
        os.close(sin_r)
        env = {k: v for k, v in os.environ.items() if k not in ("COLUMNS", "LINES")}
        os.execve(BIN, [BIN] + ARGS, env)
        os._exit(127)
    os.close(sin_r)
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))
    os.write(sin_w, b"hello\n")
    os.close(sin_w)
    return pid, fd


def drain(source: int, captured: bytearray, until: bytes | None = None) -> None:
    deadline = time.monotonic() + 2.0
    while time.monotonic() < deadline:
        if until is not None and until in captured:
            return
        if not select.select([source], [], [], 0.02)[0]:
            continue
        try:
            chunk = os.read(source, 65536)
        except OSError:
            return
        if not chunk:
            return
        captured.extend(chunk)


def reap(pid: int) -> int:
    deadline = time.monotonic() + 2.0
    while time.monotonic() < deadline:
        done, status = os.waitpid(pid, os.WNOHANG)
        if done:
            return status
        time.sleep(0.01)
    os.kill(pid, signal.SIGKILL)
    return os.waitpid(pid, 0)[1]


def exited_one(status: int) -> bool:
    return os.WIFEXITED(status) and os.WEXITSTATUS(status) == 1


def has_message(output: bytes) -> bool:
    """Any human-readable diagnostic besides the animation / cursor escapes."""
    text = output.decode("utf-8", "replace")
    lowered = text.lower()
    needles = ("error", "interrupt", "sigint", "terminated", "killed", "signal")
    return any(n in lowered for n in needles)


def main() -> int:
    pid, fd = spawn()
    captured = bytearray()
    drain(fd, captured, until=HIDE)
    os.kill(pid, signal.SIGINT)
    drain(fd, captured)
    status = reap(pid)
    os.close(fd)
    output = bytes(captured)
    checks = [
        ("exits 1", exited_one(status)),
        ("teardown hid then showed cursor", output.count(HIDE) == 1 and output.count(SHOW) == 1),
        ("SHOW after HIDE", output.find(HIDE) != -1 and output.find(SHOW) > output.find(HIDE)),
        ("no message on stdout/stderr", not has_message(output)),
    ]
    for label, passed in checks:
        print(f"  {'ok  ' if passed else 'FAIL'} {label}")
    if not exited_one(status):
        if os.WIFSIGNALED(status):
            print(f"    status: signaled {os.WTERMSIG(status)}")
        elif os.WIFEXITED(status):
            print(f"    status: exit {os.WEXITSTATUS(status)}")
        else:
            print(f"    status: {status}")
    failures = sum(not passed for _, passed in checks)
    print(f"\nsigint behavior: {'all checks passed' if not failures else f'{failures} failed'}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
