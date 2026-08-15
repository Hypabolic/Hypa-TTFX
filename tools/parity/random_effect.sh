#!/usr/bin/env bash
# --random-effect seed sweep: byte-identical frame streams vs the Rust oracle.
# A wrong registry order or ChoiceIndex shows up as a total mismatch immediately.
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "random_effect.sh: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

export COLUMNS=80 LINES=24

INPUT="Hello, World!"
SEEDS=(1 2 3 7 13 42 57 99 100 256 512 1000 1337 4096 7777 9999 12345 54321 65535 99999)

pass=0
fail=0
failed=()

for seed in "${SEEDS[@]}"; do
  ref_dump --seed "$seed" --max-frames 20 --random-effect <<< "$INPUT" > /tmp/hypa-rand-ref.dump 2>/tmp/hypa-rand-ref.err || {
    fail=$((fail + 1)); failed+=("seed=$seed ref failed"); continue; }
  "$BIN" --parity-dump --seed "$seed" --max-frames 20 --random-effect <<< "$INPUT" > /tmp/hypa-rand-ours.dump 2>/tmp/hypa-rand-ours.err || {
    fail=$((fail + 1)); failed+=("seed=$seed ours failed"); continue; }
  if cmp -s /tmp/hypa-rand-ref.dump /tmp/hypa-rand-ours.dump; then
    pass=$((pass + 1))
  else
    fail=$((fail + 1))
    failed+=("seed=$seed dump mismatch")
  fi
done

# Pure-default config: effect args must be ignored on the random path.
ref_dump --seed 42 --max-frames 10 --random-effect wipe --final-gradient-stops ff0000 <<< "$INPUT" > /tmp/hypa-rand-default-ref.dump 2>/dev/null
"$BIN" --parity-dump --seed 42 --max-frames 10 --random-effect wipe --final-gradient-stops ff0000 <<< "$INPUT" > /tmp/hypa-rand-default-ours.dump 2>/dev/null
if cmp -s /tmp/hypa-rand-default-ref.dump /tmp/hypa-rand-default-ours.dump; then
  pass=$((pass + 1))
else
  fail=$((fail + 1))
  failed+=("random-effect ignores effect args")
fi

# Empty filter set -> exit 1 with reference message.
msg=$("$BIN" --random-effect --include-effects nosucheffect <<< "$INPUT" 2>&1 >/dev/null || true)
if printf '%s' "$msg" | grep -q "No effects available after filtering."; then
  pass=$((pass + 1))
else
  fail=$((fail + 1))
  failed+=("empty filter message")
fi

echo "random-effect parity: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
