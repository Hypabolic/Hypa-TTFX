#!/usr/bin/env bash
# Byte-compare artifacts/ttfx --m0-dump against the Rust ref_m0 oracle
# for the 0007 unicode fixture (astral-plane + combining marks).
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "$ROOT/tools/parity/reference.sh"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "m0_unicode: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

FIXTURE="$ROOT/tools/parity/inputs/unicode.txt"
if [ ! -f "$FIXTURE" ]; then
  echo "m0_unicode: missing $FIXTURE" >&2
  exit 1
fi

export COLUMNS=80 LINES=24
export PATH="/usr/local/bin:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/Cellar/dotnet/10.0.400/libexec}"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

ref_m0 < "$FIXTURE" > "$tmp/ref.bin"
"$BIN" --m0-dump < "$FIXTURE" > "$tmp/ours.bin"
if cmp -s "$tmp/ref.bin" "$tmp/ours.bin"; then
  echo "m0 unicode: wipe-unicode passed"
  exit 0
fi

echo "m0 unicode: FAILED ($(cmp "$tmp/ref.bin" "$tmp/ours.bin" 2>&1 | head -1))" >&2
exit 1
