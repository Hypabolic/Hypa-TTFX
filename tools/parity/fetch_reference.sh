#!/usr/bin/env bash
# Clone ttfx at the commit pinned in REFERENCE.md, cargo build --release,
# cache the binary by commit hash, and drop tools/parity/rngdump.rs into the
# checkout as examples/rngdump.rs.
#
# Installs the oracle at reference/ttfx — never on PATH, never into artifacts/.
# The C# AOT binary lives at artifacts/ttfx; both are named ttfx and must stay
# at distinct, explicitly-referenced paths (plan §11 decision 10).
set -euo pipefail

ROOT="$(CDPATH= cd -- "${BASH_SOURCE[0]%/*}/../.." && pwd)"
cd "$ROOT"

REF_MD="$ROOT/REFERENCE.md"
SRC="$ROOT/reference/src"
DEST_BIN="$ROOT/reference/ttfx"
RNGDUMP_SRC="$ROOT/tools/parity/rngdump.rs"
LOCAL_REF="${HYPA_TTFX_LOCAL_REF:-$HOME/Development/reference-implementations/ttfx}"

need() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "fetch_reference.sh: missing required tool: $1" >&2
    exit 1
  fi
}

need git
need cargo
need rustc

if [ ! -f "$REF_MD" ]; then
  echo "fetch_reference.sh: $REF_MD is missing" >&2
  exit 1
fi

if [ ! -f "$RNGDUMP_SRC" ]; then
  echo "fetch_reference.sh: $RNGDUMP_SRC is missing" >&2
  exit 1
fi

# First fenced block in REFERENCE.md holds KEY=VALUE pins.
pin_value() {
  local key="$1"
  awk -v key="$key" '
    /^```[[:space:]]*$/ { if (++n == 1) next; exit }
    n == 1 && $0 ~ ("^" key "=") {
      sub("^" key "=", "")
      print
      exit
    }
  ' "$REF_MD"
}

PIN="$(pin_value ttfx_commit)"
REMOTE="$(pin_value ttfx_remote)"

if [ -z "$PIN" ] || [ "${#PIN}" -ne 40 ]; then
  echo "fetch_reference.sh: could not parse ttfx_commit from $REF_MD" >&2
  exit 1
fi
if [ -z "$REMOTE" ]; then
  echo "fetch_reference.sh: could not parse ttfx_remote from $REF_MD" >&2
  exit 1
fi

CACHE_BIN="$ROOT/reference/cache/$PIN/ttfx"

local_has_pin() {
  [ -d "$LOCAL_REF/.git" ] && git -C "$LOCAL_REF" cat-file -e "${PIN}^{commit}" 2>/dev/null
}

clone_from() {
  local from="$1"
  rm -rf "$SRC"
  mkdir -p "$(dirname "$SRC")"
  if [[ "$from" == https://* || "$from" == http://* || "$from" == git@* || "$from" == ssh://* ]]; then
    git clone -q --filter=blob:none "$from" "$SRC" || return 1
  else
    git clone -q "$from" "$SRC" || return 1
  fi
  git -C "$SRC" checkout -q "$PIN" || return 1
  return 0
}

ensure_checkout() {
  if [ -d "$SRC/.git" ] && [ "$(git -C "$SRC" rev-parse HEAD 2>/dev/null)" = "$PIN" ]; then
    echo "checkout already at $PIN"
    return 0
  fi

  if [ -d "$SRC/.git" ]; then
    if git -C "$SRC" cat-file -e "${PIN}^{commit}" 2>/dev/null; then
      git -C "$SRC" checkout -q "$PIN"
      echo "checkout moved to $PIN"
      return 0
    fi
    echo "existing checkout lacks $PIN; recloning"
  fi

  # Fast path: a local tree that already has the pin. Clean machines skip this
  # and clone from GitHub. Prefer HTTPS; fall back to the local tree if the
  # network clone fails.
  if local_has_pin; then
    echo "cloning from local checkout $LOCAL_REF (has $PIN)"
    if clone_from "$LOCAL_REF"; then
      return 0
    fi
    echo "local clone failed; trying GitHub" >&2
  fi

  echo "cloning from $REMOTE"
  if clone_from "$REMOTE"; then
    return 0
  fi

  echo "GitHub clone failed; trying local fallback $LOCAL_REF" >&2
  if [ -d "$LOCAL_REF/.git" ]; then
    clone_from "$LOCAL_REF"
    return 0
  fi

  echo "fetch_reference.sh: failed to fetch ttfx at $PIN" >&2
  exit 1
}

ensure_checkout

actual="$(git -C "$SRC" rev-parse HEAD)"
if [ "$actual" != "$PIN" ]; then
  echo "fetch_reference.sh: checkout HEAD is $actual, wanted $PIN" >&2
  exit 1
fi

# Always refresh the example, including on a cache hit, so
# `cargo run --example rngdump` works in the fetched tree.
mkdir -p "$SRC/examples"
cp -f "$RNGDUMP_SRC" "$SRC/examples/rngdump.rs"
echo "installed $SRC/examples/rngdump.rs"

if [ -x "$CACHE_BIN" ] && [ -s "$CACHE_BIN" ]; then
  echo "cache hit: $CACHE_BIN"
else
  echo "cache miss: cargo build --release at $PIN"
  (cd "$SRC" && cargo build --release)
  built="$SRC/target/release/ttfx"
  if [ ! -x "$built" ]; then
    echo "fetch_reference.sh: build did not produce $built" >&2
    exit 1
  fi
  mkdir -p "$(dirname "$CACHE_BIN")"
  cp -f "$built" "$CACHE_BIN"
  chmod +x "$CACHE_BIN"
  echo "cached $CACHE_BIN"
fi

mkdir -p "$(dirname "$DEST_BIN")"
cp -f "$CACHE_BIN" "$DEST_BIN"
chmod +x "$DEST_BIN"
echo "installed $DEST_BIN"
