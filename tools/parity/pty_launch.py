#!/usr/bin/env python3
"""Launch a binary under a real pty so stdout isatty is true.

stdin is fed through a pipe (so the child's input is not the pty and is not
echoed). stdout and stderr stay on the pty slave, which is what the tty
lifecycle (prep / cursor / teardown) needs. The full byte stream is written
to this process's stdout.

Usage: pty_launch.py <binary> [args...] < input > bytes
"""

from __future__ import annotations

import fcntl
import os
import pty
import select
import struct
import sys
import termios
import tty


def _wait_status(status: int) -> int:
    if os.WIFEXITED(status):
        return os.WEXITSTATUS(status)
    if os.WIFSIGNALED(status):
        return 128 + os.WTERMSIG(status)
    return 1


def _drain(master: int) -> None:
    while True:
        try:
            chunk = os.read(master, 65536)
        except OSError:
            return
        if not chunk:
            return
        sys.stdout.buffer.write(chunk)
        sys.stdout.buffer.flush()


def main() -> int:
    if len(sys.argv) < 2:
        sys.stderr.write("usage: pty_launch.py <binary> [args...]\n")
        return 2

    argv = sys.argv[1:]
    binary = argv[0]
    if not os.path.isfile(binary) or not os.access(binary, os.X_OK):
        sys.stderr.write(f"pty_launch.py: not an executable: {binary}\n")
        return 1

    stdin_data = sys.stdin.buffer.read()

    sin_r, sin_w = os.pipe()
    pid, master = pty.fork()
    if pid == 0:
        os.close(sin_w)
        os.dup2(sin_r, 0)
        os.close(sin_r)
        # COLUMNS/LINES would win over the tty we just sized.
        env = {k: v for k, v in os.environ.items() if k not in ("COLUMNS", "LINES")}
        os.execve(binary, argv, env)
        os._exit(127)

    os.close(sin_r)
    try:
        fcntl.ioctl(master, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))
        tty.setraw(master)
    except OSError:
        pass

    try:
        if stdin_data:
            os.write(sin_w, stdin_data)
    except OSError:
        pass
    os.close(sin_w)

    while True:
        try:
            ready, _, _ = select.select([master], [], [], 0.05)
        except InterruptedError:
            continue
        if ready:
            try:
                chunk = os.read(master, 65536)
            except OSError:
                break
            if not chunk:
                break
            sys.stdout.buffer.write(chunk)
            sys.stdout.buffer.flush()
            continue
        done, status = os.waitpid(pid, os.WNOHANG)
        if done:
            _drain(master)
            return _wait_status(status)

    _, status = os.waitpid(pid, 0)
    _drain(master)
    return _wait_status(status)


if __name__ == "__main__":
    raise SystemExit(main())
