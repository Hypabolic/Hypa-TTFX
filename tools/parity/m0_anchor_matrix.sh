#!/usr/bin/env bash
# Generated M0 anchoring matrix: byte-compare artifacts/ttfx --m0-dump against
# the Rust ref_m0 oracle.
#
# A reference bump re-runs this script; the 9×9 × clipped/unclipped product is
# generated here rather than re-derived by hand.
#
# Coverage:
#   - all nine --anchor-canvas × all nine --anchor-text, each clipped and unclipped
#   - the 14 inherited option-interaction variants from ttfx m0_matrix.sh
#   - --wrap-text and non-default --tab-width crossed with non-sw anchors
#   - --canvas-width 0 / -1 and --ignore-terminal-dimensions
#   - a dump assertion that non-sw anchors put leading blanks *inside* the frame
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "m0_anchor_matrix: $BIN is missing. Run bin/build first." >&2
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

anchors=(n ne e se s sw w nw c)

# Clipped: canvas smaller than typical input, so anchoring drops out-of-canvas
# cells. Unclipped: ignore-terminal-dimensions so the full canvas is the frame
# (no visible_* clamp) and text-anchor leading blanks stay inside it.
clipped_args=(--canvas-width 12 --canvas-height 4)
unclipped_args=(--ignore-terminal-dimensions --canvas-width 40 --canvas-height 12)

echo "m0 anchor matrix: 9×9 × clipped/unclipped"
for file in "$inputs"/*.txt; do
  base=$(basename "$file" .txt)
  for ac in "${anchors[@]}"; do
    for at in "${anchors[@]}"; do
      cmp_case "$base/clip/${ac}x${at}" "$file" "${clipped_args[@]}" --anchor-canvas "$ac" --anchor-text "$at"
      cmp_case "$base/unclip/${ac}x${at}" "$file" "${unclipped_args[@]}" --anchor-canvas "$ac" --anchor-text "$at"
    done
  done
done

# Inherited 14 option-interaction variants (ttfx tools/parity/m0_matrix.sh:29-44).
inherited=(
  "default|"
  "anchor_c|--anchor-canvas c --anchor-text c --canvas-width 60 --canvas-height 20"
  "anchor_ne|--anchor-canvas ne --anchor-text ne --canvas-width 60 --canvas-height 20"
  "anchor_mixed|--anchor-canvas n --anchor-text se --canvas-width 40 --canvas-height 12"
  "canvas_terminal|--canvas-width 0 --canvas-height 0"
  "canvas_small|--canvas-width 12 --canvas-height 4"
  "wrap|--wrap-text --canvas-width 20"
  "tab8|--tab-width 8"
  "xterm|--xterm-colors"
  "nocolor|--no-color"
  "always|--existing-color-handling always"
  "always_xterm|--existing-color-handling always --xterm-colors"
  "always_nocolor|--existing-color-handling always --no-color"
  "ignore_dims|--ignore-terminal-dimensions --canvas-width 120 --canvas-height 40"
)

echo "m0 anchor matrix: inherited 14 variants"
for file in "$inputs"/*.txt; do
  base=$(basename "$file" .txt)
  for spec in "${inherited[@]}"; do
    variant=${spec%%|*}
    args=${spec#*|}
    # shellcheck disable=SC2086
    cmp_case "$base/inherited/$variant" "$file" $args
  done
done

# wrap-text and non-default tab-width crossed with non-sw anchoring.
echo "m0 anchor matrix: wrap-text / tab-width × anchors"
for at in "${anchors[@]}"; do
  cmp_case "wide/wrap-c-x${at}" "$inputs/wide.txt" --wrap-text --canvas-width 20 --anchor-canvas c --anchor-text "$at"
  cmp_case "tabs/tab8-ne-x${at}" "$inputs/tabs.txt" --tab-width 8 --canvas-width 40 --canvas-height 6 --anchor-canvas ne --anchor-text "$at"
done
cmp_case "wide/wrap-tab8-n-xse" "$inputs/wide.txt" --wrap-text --tab-width 8 --canvas-width 16 --anchor-canvas n --anchor-text se
cmp_case "tabs/wrap-tab8-n-xse" "$inputs/tabs.txt" --wrap-text --tab-width 8 --canvas-width 16 --anchor-canvas n --anchor-text se
cmp_case "multiline/wrap-tab8-c-xne" "$inputs/multiline.txt" --wrap-text --tab-width 8 --canvas-width 20 --anchor-canvas c --anchor-text ne

# -1 / 0 canvas sizing and ignore-terminal-dimensions overwriting term dims.
echo "m0 anchor matrix: canvas 0/-1 and ignore-terminal-dimensions"
for file in "$inputs/simple.txt" "$inputs/multiline.txt" "$inputs/wide.txt"; do
  base=$(basename "$file" .txt)
  cmp_case "$base/canvas0" "$file" --canvas-width 0 --canvas-height 0
  cmp_case "$base/canvas-1" "$file" --canvas-width -1 --canvas-height -1
  cmp_case "$base/ignore-1" "$file" --ignore-terminal-dimensions --canvas-width -1 --canvas-height -1
  cmp_case "$base/ignore0" "$file" --ignore-terminal-dimensions --canvas-width 0 --canvas-height 0
  cmp_case "$base/canvas0-c-xne" "$file" --canvas-width 0 --canvas-height 0 --anchor-canvas c --anchor-text ne
  cmp_case "$base/ignore-1-ne-xc" "$file" --ignore-terminal-dimensions --canvas-width -1 --canvas-height -1 --anchor-canvas ne --anchor-text c
done

# Visible-bounds clamp: canvas larger than the 80×24 terminal.
echo "m0 anchor matrix: visible-bounds clamp"
for ac in "${anchors[@]}"; do
  cmp_case "simple/clamp/${ac}xsw" "$inputs/simple.txt" --canvas-width 100 --canvas-height 30 --anchor-canvas "$ac" --anchor-text sw
done

# Non-southwest anchors produce leading blanks *inside* the frame.
echo "m0 anchor matrix: leading-blanks dump"
printf 'Hi' | "$BIN" --m0-dump --ignore-terminal-dimensions --canvas-width 10 --canvas-height 5 --anchor-text ne > "$tmp/ne-ours.bin"
printf 'Hi' | ref_m0 --ignore-terminal-dimensions --canvas-width 10 --canvas-height 5 --anchor-text ne > "$tmp/ne-ref.bin"
if ! cmp -s "$tmp/ne-ref.bin" "$tmp/ne-ours.bin"; then
  fail=$((fail + 1))
  failed+=("leading-blanks/ne ($(cmp "$tmp/ne-ref.bin" "$tmp/ne-ours.bin" 2>&1 | head -1))")
else
  python3 - "$tmp/ne-ours.bin" <<'PY'
import sys
path = sys.argv[1]
data = open(path, "rb").read()
rows = data.split(b"\n")
if rows and rows[-1] == b"":
    rows = rows[:-1]
if len(rows) != 5 or any(len(r) != 10 for r in rows):
    raise SystemExit(f"unexpected geometry: {[len(r) for r in rows]}")
if rows[0] != b"        Hi":
    raise SystemExit(f"expected 8 leading spaces then Hi, got {rows[0]!r}")
if any(r != b"          " for r in rows[1:]):
    raise SystemExit(f"expected blank rows under NE text, got {rows!r}")
print("leading-blanks: NE text is 8 spaces + 'Hi' on the first frame row")
PY
  pass=$((pass + 1))
fi

printf 'Hi' | "$BIN" --m0-dump --ignore-terminal-dimensions --canvas-width 10 --canvas-height 5 --anchor-text sw > "$tmp/sw-ours.bin"
python3 - "$tmp/ne-ours.bin" "$tmp/sw-ours.bin" <<'PY'
import sys
ne = open(sys.argv[1], "rb").read().split(b"\n")
sw = open(sys.argv[2], "rb").read().split(b"\n")
if ne and ne[-1] == b"":
    ne = ne[:-1]
if sw and sw[-1] == b"":
    sw = sw[:-1]
if ne[0] == sw[0]:
    raise SystemExit("NE and SW first rows should differ (leading blanks)")
if not ne[0].startswith(b"        "):
    raise SystemExit(f"NE first row lacks leading blanks: {ne[0]!r}")
if not sw[-1].startswith(b"Hi"):
    raise SystemExit(f"SW last row should start with Hi: {sw[-1]!r}")
print("leading-blanks: SW has no leading blanks; NE does (inside the frame)")
PY
pass=$((pass + 1))

echo "m0 anchor matrix: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
