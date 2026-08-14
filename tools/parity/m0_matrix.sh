#!/usr/bin/env bash
# Byte-compare artifacts/ttfx --m0-dump against the Rust ref_m0 oracle
# across the inherited colour-bearing m0_matrix variants.
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "m0_matrix: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

export COLUMNS=80 LINES=24
export PATH="/usr/local/bin:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/Cellar/dotnet/10.0.400/libexec}"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

inputs="$tmp/inputs"
mkdir -p "$inputs"
printf 'Hello, World!' > "$inputs/simple.txt"
printf 'line one\nsecond line is longer\n\tindented\nshort\n' > "$inputs/multiline.txt"
printf 'a\n\n\nb with trailing spaces   \n  leading\n' > "$inputs/ragged.txt"
printf '\x1b[31mred\x1b[0m plain \x1b[1;32mboldgreen\x1b[0m\n\x1b[38;2;255;0;128mrgb\x1b[0m \x1b[48;5;42mbg8\x1b[0m\n' > "$inputs/colored.txt"
printf '\x1b[93mbright\x1b[39mdefault\n\x1b[34mblue\x1b[1mboldblue\x1b[22munbold\x1b[0m\n' > "$inputs/colorstate.txt"
printf 'over\rwritten\nnext\x1b[2Cgap\n' > "$inputs/cursor.txt"
printf 'tab\ttab\t\tend\n' > "$inputs/tabs.txt"
python3 -c "print('wide line ' * 12)" > "$inputs/wide.txt"

pass=0
fail=0
failed=()

cmp_case() {
  local name="$1" file="$2"
  shift 2
  set +e
  ref_m0 "$@" < "$file" > "$tmp/ref.bin" 2>"$tmp/ref.err"
  local ref_rc=$?
  "$BIN" --m0-dump "$@" < "$file" > "$tmp/ours.bin" 2>"$tmp/ours.err"
  local ours_rc=$?
  set -e
  if [ "$ref_rc" -ne "$ours_rc" ]; then
    fail=$((fail + 1))
    failed+=("$name (exit: ref=$ref_rc ours=$ours_rc)")
    return
  fi
  if [ "$ref_rc" -ne 0 ]; then
    pass=$((pass + 1))
    return
  fi
  if cmp -s "$tmp/ref.bin" "$tmp/ours.bin"; then
    pass=$((pass + 1))
  else
    fail=$((fail + 1))
    failed+=("$name ($(cmp "$tmp/ref.bin" "$tmp/ours.bin" 2>&1 | head -1))")
  fi
}

# name|args  — bash 3.2 has no associative arrays
variants=(
  "default|"
  "xterm|--xterm-colors"
  "nocolor|--no-color"
  "always|--existing-color-handling always"
  "always_xterm|--existing-color-handling always --xterm-colors"
  "always_nocolor|--existing-color-handling always --no-color"
  "bgcolor|--terminal-background-color ff0000"
  "anchor_c|--anchor-canvas c --anchor-text c --canvas-width 60 --canvas-height 20"
  "tab8|--tab-width 8"
  "ignore_dims|--ignore-terminal-dimensions --canvas-width 120 --canvas-height 40"
)

for file in "$inputs"/*.txt; do
  base=$(basename "$file" .txt)
  for spec in "${variants[@]}"; do
    variant=${spec%%|*}
    args=${spec#*|}
    # shellcheck disable=SC2086
    cmp_case "$base/$variant" "$file" $args
  done
done

echo "m0 matrix: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
