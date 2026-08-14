#!/usr/bin/env bash
# Byte-compare artifacts/ttfx --m0-dump against the Rust ref_m0 oracle
# for the ASCII fixtures at default options (and the 0002 canvas pair).
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "m0_ascii: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

export COLUMNS=80 LINES=24
export PATH="/usr/local/bin:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/Cellar/dotnet/10.0.400/libexec}"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

inputs="$tmp/inputs"
mkdir -p "$inputs"
printf 'hello world\nsecond line\n' > "$inputs/two-line.txt"
printf 'Hello, World!\nThis is ttfx.\nRust vs Python' > "$inputs/basic.txt"
printf 'x' > "$inputs/single.txt"
python3 -c "
lines = ['The quick brown fox jumps over the lazy dog %d' % i for i in range(8)]
print('\n'.join(lines))" > "$inputs/paragraph.txt"

pass=0
fail=0
failed=()

cmp_case() {
  local name="$1" file="$2"
  shift 2
  ref_m0 "$@" < "$file" > "$tmp/ref.bin"
  "$BIN" --m0-dump "$@" < "$file" > "$tmp/ours.bin"
  if cmp -s "$tmp/ref.bin" "$tmp/ours.bin"; then
    pass=$((pass + 1))
  else
    fail=$((fail + 1))
    failed+=("$name ($(cmp "$tmp/ref.bin" "$tmp/ours.bin" 2>&1 | head -1))")
  fi
}

cmp_case two-line-default "$inputs/two-line.txt"
cmp_case basic-default "$inputs/basic.txt"
cmp_case single-default "$inputs/single.txt"
cmp_case paragraph-default "$inputs/paragraph.txt"

cmp_case two-line-canvas "$inputs/two-line.txt" --ignore-terminal-dimensions --canvas-width 20 --canvas-height 4
cmp_case basic-canvas "$inputs/basic.txt" --ignore-terminal-dimensions --canvas-width 20 --canvas-height 4
cmp_case single-canvas "$inputs/single.txt" --ignore-terminal-dimensions --canvas-width 20 --canvas-height 4
cmp_case paragraph-canvas "$inputs/paragraph.txt" --ignore-terminal-dimensions --canvas-width 20 --canvas-height 4

echo "m0 ascii: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
