#!/usr/bin/env bash
# Tty byte-stream parity for wipe only: prep / per-frame cursor / teardown
# compared against ref_tty (pty_launch.py). Our binary is also launched under
# a pty so isatty is true. --frame-rate 0 --virtual-clock on both sides.
set -uo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "tty_compare: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

export COLUMNS=80 LINES=24
export PATH="/usr/local/bin:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/Cellar/dotnet/10.0.400/libexec}"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

inputs="$tmp/inputs"
mkdir -p "$inputs"
printf 'Hello, World!\nThis is ttfx.\nRust vs Python' > "$inputs/basic.txt"

pass=0
fail=0
failed=()

run_case() {
  local name="$1"
  shift
  local ref_rc=0 ours_rc=0
  ref_tty --seed 42 "$@" < "$inputs/basic.txt" > "$tmp/ref.bytes" 2>"$tmp/ref.err" || ref_rc=$?
  python3 "$ROOT/tools/parity/pty_launch.py" "$BIN" --frame-rate 0 --virtual-clock --seed 42 "$@" \
    < "$inputs/basic.txt" > "$tmp/ours.bytes" 2>"$tmp/ours.err" || ours_rc=$?
  if [ "$ref_rc" -ne "$ours_rc" ]; then
    fail=$((fail + 1))
    failed+=("$name (exit ref=$ref_rc ours=$ours_rc)")
    return
  fi
  if cmp -s "$tmp/ref.bytes" "$tmp/ours.bytes"; then
    pass=$((pass + 1))
  else
    fail=$((fail + 1))
    failed+=("$name ($(cmp "$tmp/ref.bytes" "$tmp/ours.bytes" 2>&1 | head -1))")
  fi
}

run_case tty-wipe wipe
run_case tty-no-eol --no-eol wipe
run_case tty-no-restore-cursor --no-restore-cursor wipe
run_case tty-reuse-canvas --reuse-canvas wipe

echo "tty byte-stream parity: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
