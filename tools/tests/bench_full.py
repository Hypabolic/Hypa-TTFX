"""Fair hypa-ttfx vs ttfx vs upstream-TTE benchmark.

All three sides run their real user-facing command (no parity shim). Frame pacing
is disabled on both sides so this measures render throughput, not sleep().

matrix and thunderstorm are reported separately: they gate on wall-clock time.

Usage: bench_full.py [repeats]
"""

from __future__ import annotations

import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HYPA = ROOT / "artifacts/ttfx"
RUST = ROOT / "reference/ttfx"
REF = ROOT / "reference/tte"
REPEATS = int(sys.argv[1]) if len(sys.argv) > 1 else 3
CLOCK_BOUND = {"matrix", "thunderstorm"}

COLS = os.environ.get("TTFX_BENCH_COLS", "100")
LINES = os.environ.get("TTFX_BENCH_LINES", "30")

ENV = {**os.environ, "COLUMNS": COLS, "LINES": LINES, "PYTHONPATH": str(REF)}


def effects() -> list[str]:
    out = subprocess.run([str(RUST), "--help"], capture_output=True, text=True).stdout
    names, grab = [], False
    for line in out.splitlines():
        if line.startswith("Commands:"):
            grab = True
            continue
        if line.startswith("Options:"):
            break
        if grab and line.startswith("  ") and line.strip():
            n = line.split()[0]
            if n != "help":
                names.append(n)
    return names


def best_of(cmd: list[str], data: bytes) -> float:
    times = []
    for _ in range(REPEATS):
        t = time.monotonic()
        subprocess.run(cmd, input=data, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=ENV)
        times.append((time.monotonic() - t) * 1000)
    return min(times)


def frames_of(bin_path: Path, effect: str, data: bytes) -> int:
    r = subprocess.run(
        [str(bin_path), "--seed", "1", "--frame-rate", "0", "--virtual-clock", "--parity-dump", effect],
        input=data,
        capture_output=True,
        env=ENV,
    )
    return int(r.stderr.decode().strip().rsplit("=", 1)[-1] or 0)


def main() -> int:
    if not HYPA.is_file():
        print(f"{HYPA} missing — run bin/build first", file=sys.stderr)
        return 1
    if not RUST.is_file():
        print(f"{RUST} missing — run tools/parity/fetch_reference.sh first", file=sys.stderr)
        return 1

    filler = "the quick brown fox jumps over the lazy dog"
    if os.environ.get("TTFX_BENCH_FILL") == "1":
        rows, width = max(1, int(LINES) - 4), max(20, int(COLS) - 10)
        lines = [f"benchmark line {i:03d} — {filler}" for i in range(rows)]
        lines = [(l * (width // len(l) + 1))[:width] for l in lines]
    else:
        lines = [f"benchmark line {i:03d} — {filler}" for i in range(20)]
    text = "\n".join(lines)
    data = text.encode()

    print(f"canvas: {COLS}x{LINES} · input: {len(text.splitlines())} lines · best of {REPEATS}\n")

    tiny = b"x"
    hypa_start = best_of([str(HYPA), "--seed", "1", "--frame-rate", "0", "wipe"], tiny)
    rs_start = best_of([str(RUST), "--seed", "1", "--frame-rate", "0", "wipe"], tiny)
    py_start = best_of([sys.executable, "-m", "terminaltexteffects", "--frame-rate", "0", "wipe"], tiny)
    print(
        f"{'startup (1 char, wipe)':<22} hypa {hypa_start:7.1f} ms   rust {rs_start:7.1f} ms   "
        f"python {py_start:8.1f} ms\n"
    )

    rows_out = []
    ratios = []
    for e in effects():
        hypa = best_of([str(HYPA), "--seed", "1", "--frame-rate", "0", e], data)
        rs = best_of([str(RUST), "--seed", "1", "--frame-rate", "0", e], data)
        py = best_of([sys.executable, "-m", "terminaltexteffects", "--frame-rate", "0", e], data)
        n = frames_of(HYPA, e, data)
        rows_out.append((e, hypa, rs, py, py / rs if rs else 0, n))
        if e not in CLOCK_BOUND:
            ratios.append(py / rs if rs else 0)

    rows_out.sort(key=lambda r: -r[4])
    print(f"{'effect':<17}{'hypa ms':>9}{'rust ms':>9}{'python ms':>11}{'py/rust':>9}{'frames':>8}")
    print("-" * 72)
    for e, hypa, rs, py, ratio, n in rows_out:
        mark = " *" if e in CLOCK_BOUND else ""
        print(f"{e+mark:<17}{hypa:9.1f}{rs:9.1f}{py:11.1f}{ratio:8.1f}x{n:8d}")

    ratios.sort()
    mid = ratios[len(ratios) // 2]
    print("-" * 72)
    print(f"median python/rust speedup (35 non-clock-bound): {mid:.1f}x")
    print("* clock-bound: gated on wall time")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
