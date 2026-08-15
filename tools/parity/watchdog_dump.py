#!/usr/bin/env python3
"""Run a parity dump with wall-clock and frame-count watchdogs.

Exit 124 = wall-clock exceeded, 125 = frame cap exceeded.
"""
from __future__ import annotations

import os
import subprocess
import sys
import time
from pathlib import Path

WALL_SEC = int(os.environ.get("HYPA_TTFX_WATCHDOG_SEC", "300"))
FRAME_CAP = int(os.environ.get("HYPA_TTFX_WATCHDOG_FRAMES", "50000"))


def count_parity_frames(path: Path) -> int:
    """Count length-prefixed frames in a --parity-dump stream."""
    if not path.is_file():
        return 0

    data = path.read_bytes()
    pos = 0
    count = 0
    n = len(data)

    while pos < n:
        line_start = pos
        while pos < n and data[pos : pos + 1] != b"\n":
            pos += 1
        if pos >= n:
            break

        length_line = data[line_start:pos]
        pos += 1  # skip newline after length

        try:
            length = int(length_line.decode("ascii"))
        except ValueError:
            break

        if length < 0 or pos + length > n:
            break

        pos += length
        if pos >= n or data[pos : pos + 1] != b"\n":
            break

        pos += 1  # skip newline after frame payload
        count += 1

    return count


def main() -> int:
    if len(sys.argv) < 3:
        print("usage: watchdog_dump.py BIN --parity-dump [args...]", file=sys.stderr)
        return 2

    bin_path = sys.argv[1]
    args = sys.argv[2:]
    out = Path(f"/tmp/hypa-watchdog-{os.getpid()}.dump")
    err = Path(f"/tmp/hypa-watchdog-{os.getpid()}.err")
    try:
        with out.open("wb") as fout, err.open("wb") as ferr:
            proc = subprocess.Popen(
                [bin_path, *args],
                stdin=sys.stdin,
                stdout=fout,
                stderr=ferr,
            )
            start = time.monotonic()
            while proc.poll() is None:
                if time.monotonic() - start > WALL_SEC:
                    proc.kill()
                    proc.wait()
                    print(f"watchdog: wall clock {WALL_SEC}s exceeded", file=sys.stderr)
                    return 124
                if count_parity_frames(out) > FRAME_CAP:
                    proc.kill()
                    proc.wait()
                    print(f"watchdog: frame cap {FRAME_CAP} exceeded", file=sys.stderr)
                    return 125
                time.sleep(0.05)
            rc = proc.returncode or 0
        sys.stdout.buffer.write(out.read_bytes())
        sys.stderr.buffer.write(err.read_bytes())
        return rc
    finally:
        out.unlink(missing_ok=True)
        err.unlink(missing_ok=True)


if __name__ == "__main__":
    raise SystemExit(main())
