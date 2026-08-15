#!/usr/bin/env bash
# Deterministic rebuild-after hook: RNG continuity across the parity rebuild path.
# Rust ttfx has no --rebuild-after flag; this compares two identical hypa-ttfx runs
# and verifies rebuild-after differs from a no-rebuild run (effect restarts, RNG continues).
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "rebuild_after.sh: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

export COLUMNS=80 LINES=24
INPUT="Hello, World!"

pass=0
fail=0
failed=()

count_parity_frames() {
  python3 - "$1" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
data = path.read_bytes()
pos = 0
count = 0
n = len(data)
while pos < n:
    line_start = pos
    while pos < n and data[pos:pos + 1] != b"\n":
        pos += 1
    if pos >= n:
        break
    try:
        length = int(data[line_start:pos].decode("ascii"))
    except ValueError:
        break
    pos += 1
    if length < 0 or pos + length > n:
        break
    pos += length
    if pos >= n or data[pos:pos + 1] != b"\n":
        break
    pos += 1
    count += 1
print(count)
PY
}

# Deterministic: two runs with the same rebuild-after args must match.
"$BIN" --parity-dump --seed 1 --rebuild-after 5 --ignore-terminal-dimensions --canvas-width 40 --canvas-height 12 wipe <<< "$INPUT" > /tmp/hypa-rebuild-a.dump 2>/dev/null
"$BIN" --parity-dump --seed 1 --rebuild-after 5 --ignore-terminal-dimensions --canvas-width 40 --canvas-height 12 wipe <<< "$INPUT" > /tmp/hypa-rebuild-b.dump 2>/dev/null
if cmp -s /tmp/hypa-rebuild-a.dump /tmp/hypa-rebuild-b.dump; then
  pass=$((pass + 1))
else
  fail=$((fail + 1))
  failed+=("rebuild-after not deterministic")
fi

# Rebuild path must differ from an uninterrupted run (effect restarts mid-stream).
"$BIN" --parity-dump --seed 1 --ignore-terminal-dimensions --canvas-width 40 --canvas-height 12 wipe <<< "$INPUT" > /tmp/hypa-norebuild.dump 2>/dev/null
if cmp -s /tmp/hypa-rebuild-a.dump /tmp/hypa-norebuild.dump; then
  fail=$((fail + 1))
  failed+=("rebuild-after identical to no-rebuild (hook inert?)")
else
  pass=$((pass + 1))
fi

# Hook must emit at least the rebuild frame count plus continuation.
frames=$(count_parity_frames /tmp/hypa-rebuild-a.dump)
if [ "${frames:-0}" -gt 5 ]; then
  pass=$((pass + 1))
else
  fail=$((fail + 1))
  failed+=("rebuild-after too few frames ($frames)")
fi

# --max-frames caps the first rebuild-after pass when M < N (min compose; no second pass).
"$BIN" --parity-dump --seed 1 --max-frames 3 --rebuild-after 5 --ignore-terminal-dimensions --canvas-width 40 --canvas-height 12 wipe <<< "$INPUT" > /tmp/hypa-rebuild-cap-lt.dump 2>/tmp/hypa-rebuild-cap-lt.err
lt_reports=$(grep -c '^frames=' /tmp/hypa-rebuild-cap-lt.err || true)
lt_frames=$(count_parity_frames /tmp/hypa-rebuild-cap-lt.dump)
if [ "$lt_reports" -eq 1 ] && [ "$lt_frames" -eq 3 ]; then
  pass=$((pass + 1))
else
  fail=$((fail + 1))
  failed+=("M<N max-frames compose (reports=$lt_reports frames=$lt_frames)")
fi

# --max-frames caps total across both passes when M > N (remaining budget on second pass).
"$BIN" --parity-dump --seed 1 --max-frames 10 --rebuild-after 5 --ignore-terminal-dimensions --canvas-width 40 --canvas-height 12 wipe <<< "$INPUT" > /tmp/hypa-rebuild-cap-gt.dump 2>/tmp/hypa-rebuild-cap-gt.err
gt_reports=$(grep -c '^frames=' /tmp/hypa-rebuild-cap-gt.err || true)
gt_frames=$(count_parity_frames /tmp/hypa-rebuild-cap-gt.dump)
if [ "$gt_reports" -eq 2 ] && [ "$gt_frames" -eq 10 ]; then
  pass=$((pass + 1))
else
  fail=$((fail + 1))
  failed+=("M>N max-frames compose (reports=$gt_reports frames=$gt_frames want 2 reports 10 frames)")
fi

# --max-frames equals rebuild-after: no second pass (budget exhausted; avoid dumpLimit=0).
"$BIN" --parity-dump --seed 1 --max-frames 5 --rebuild-after 5 --ignore-terminal-dimensions --canvas-width 40 --canvas-height 12 wipe <<< "$INPUT" > /tmp/hypa-rebuild-cap-eq.dump 2>/tmp/hypa-rebuild-cap-eq.err
eq_reports=$(grep -c '^frames=' /tmp/hypa-rebuild-cap-eq.err || true)
eq_frames=$(count_parity_frames /tmp/hypa-rebuild-cap-eq.dump)
if [ "$eq_reports" -eq 1 ] && [ "$eq_frames" -eq 5 ]; then
  pass=$((pass + 1))
else
  fail=$((fail + 1))
  failed+=("M==N max-frames compose (reports=$eq_reports frames=$eq_frames want 1 report 5 frames)")
fi

echo "rebuild-after: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
