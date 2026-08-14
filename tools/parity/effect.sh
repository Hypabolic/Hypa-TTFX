#!/usr/bin/env bash
# Effect parity: bounded (400) and unbounded-to-completion dumps vs the Rust
# oracle, at seeds 42 and 1337, for every ${PREFIX}-* line in cases.txt.
# A matching prefix is not a pass — full dumps and frames=N must match.
set -uo pipefail

if [ "$#" -lt 1 ] || [ -z "${1:-}" ]; then
  echo "usage: effect.sh PREFIX" >&2
  echo "  PREFIX is the cases.txt name prefix (wipe, bouncyballs, ...)" >&2
  exit 2
fi

PREFIX="$1"

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "effect.sh: $BIN is missing. Run bin/build first." >&2
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
printf 'x' > "$inputs/single.txt"
printf '\x1b[31mred\x1b[0m plain \x1b[1;32mboldgreen\x1b[0m\n\x1b[38;2;255;0;128mrgb\x1b[0m \x1b[48;5;42mbg8\x1b[0m\n' > "$inputs/colored.txt"
python3 -c "
lines = ['The quick brown fox jumps over the lazy dog %d' % i for i in range(8)]
print('\n'.join(lines))" > "$inputs/paragraph.txt"
cp "$ROOT/tools/parity/inputs/unicode.txt" "$inputs/unicode.txt"

pass=0
fail=0
failed=()

run_pair() {
  local label="$1" input="$2"
  shift 2
  # remaining: extra flags then the case args (unquoted word-split like run_suite)
  local ref_rc=0 ours_rc=0
  ref_dump "$@" < "$input" > "$tmp/ref.dump" 2>"$tmp/ref.err" || ref_rc=$?
  "$BIN" --parity-dump "$@" < "$input" > "$tmp/ours.dump" 2>"$tmp/ours.err" || ours_rc=$?
  if [ "$ref_rc" -ne "$ours_rc" ]; then
    fail=$((fail + 1))
    failed+=("$label (exit ref=$ref_rc ours=$ours_rc)")
    return
  fi
  if ! cmp -s "$tmp/ref.dump" "$tmp/ours.dump"; then
    fail=$((fail + 1))
    failed+=("$label (first diff: $(cmp "$tmp/ref.dump" "$tmp/ours.dump" 2>&1 | head -1))")
    return
  fi
  local ref_frames ours_frames
  ref_frames=$(grep -E '^frames=' "$tmp/ref.err" | tail -1)
  ours_frames=$(grep -E '^frames=' "$tmp/ours.err" | tail -1)
  if [ "$ref_frames" != "$ours_frames" ]; then
    fail=$((fail + 1))
    failed+=("$label (stderr $ref_frames vs $ours_frames)")
    return
  fi
  pass=$((pass + 1))
}

while IFS='|' read -r name input args; do
  [ -z "${name:-}" ] && continue
  case "$name" in \#*) continue ;; esac
  case "$name" in "${PREFIX}"-*) ;; *) continue ;; esac

  for seed in 42 1337; do
    # word-split $args the same way run_suite.sh does
    # shellcheck disable=SC2086
    run_pair "$name seed=$seed bounded" "$inputs/$input" --seed "$seed" --max-frames 400 $args
    # shellcheck disable=SC2086
    run_pair "$name seed=$seed unbounded" "$inputs/$input" --seed "$seed" $args
  done
done < "$ROOT/tools/parity/cases.txt"

echo "${PREFIX} parity: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
