#!/usr/bin/env bash
# colorshift parity — skip unbounded for --cycles 0 (never terminates; colorshift.rs:94).
set -uo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "colorshift.sh: $BIN is missing. Run bin/build first." >&2
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
  case "$name" in colorshift-*) ;; *) continue ;; esac

  # --cycles 0 loops forever by design; exclude unbounded runs.
  skip_unbounded=0
  case " $args " in
    *" --cycles 0 "*) skip_unbounded=1 ;;
  esac

  for seed in 42 1337; do
    # shellcheck disable=SC2086
    run_pair "$name seed=$seed bounded" "$inputs/$input" --seed "$seed" --max-frames 400 $args
    if [ "$skip_unbounded" -eq 0 ]; then
      # shellcheck disable=SC2086
      run_pair "$name seed=$seed unbounded" "$inputs/$input" --seed "$seed" $args
    fi
  done
done < "$ROOT/tools/parity/cases.txt"

echo "colorshift parity: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
