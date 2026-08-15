#!/usr/bin/env bash
# Adversarial ANSI m0 parity: cursor overwrites, ignored SGR params, private
# modes, trailing colored cells, and malformed CSI rejection.
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "ansi_corpus.sh: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

export COLUMNS=80 LINES=24

pass=0
fail=0
failed=()

cmp_m0() {
  local name="$1" file="$2"
  local ref_rc=0 ours_rc=0
  ref_m0 < "$file" > /tmp/hypa-ansi-ref.bin 2>/tmp/hypa-ansi-ref.err || ref_rc=$?
  "$BIN" --m0-dump < "$file" > /tmp/hypa-ansi-ours.bin 2>/tmp/hypa-ansi-ours.err || ours_rc=$?
  if [ "$ref_rc" -ne "$ours_rc" ]; then
    fail=$((fail + 1))
    failed+=("$name (exit ref=$ref_rc ours=$ours_rc)")
    return
  fi
  if [ "$ref_rc" -eq 0 ]; then
    if cmp -s /tmp/hypa-ansi-ref.bin /tmp/hypa-ansi-ours.bin; then
      pass=$((pass + 1))
    else
      fail=$((fail + 1))
      failed+=("$name (byte mismatch)")
    fi
  else
    pass=$((pass + 1))
  fi
}

for file in "$ROOT/tools/parity/inputs/adversarial"/*.txt; do
  base=$(basename "$file" .txt)
  cmp_m0 "$base" "$file"
done

echo "ansi corpus: $pass passed, $fail failed"
if [ "$fail" -gt 0 ]; then
  printf 'FAILED: %s\n' "${failed[@]}"
  exit 1
fi
