#!/usr/bin/env bash
# Per-RID transcendental measurement: dump easing at 1e-3 steps
# and the geometry lattice from both the AOT-published C# binary and the
# Rust reference examples, then compare raw floats vs the quantized lattice.
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
cd "$ROOT"

export PATH="/usr/local/bin:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/Cellar/dotnet/10.0.400/libexec}"

BIN="$ROOT/artifacts/ttfx"
if [ ! -x "$BIN" ]; then
  echo "measure_transcendentals: $BIN is missing. Run bin/build first." >&2
  exit 1
fi

"$ROOT/tools/parity/fetch_reference.sh"

REF_SRC="$ROOT/reference/src"
if [ ! -d "$REF_SRC" ]; then
  echo "measure_transcendentals: $REF_SRC missing after fetch" >&2
  exit 1
fi

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

echo "dumping C# AOT easing/geometry..."
"$BIN" --easing-golden-dump > "$tmp/cs_easing.bin"
"$BIN" --geometry-golden-dump > "$tmp/cs_geometry.txt"

echo "dumping Rust easing/geometry examples..."
(cd "$REF_SRC" && cargo run --quiet --release --example easingdump) > "$tmp/rs_easing.bin"
(cd "$REF_SRC" && cargo run --quiet --release --example geometrydump) > "$tmp/rs_geometry.txt"

python3 - "$tmp" <<'PY'
import struct
import sys
from pathlib import Path

tmp = Path(sys.argv[1])
cs_e = (tmp / "cs_easing.bin").read_bytes()
rs_e = (tmp / "rs_easing.bin").read_bytes()
print(f"easing bytes csharp={len(cs_e)} rust={len(rs_e)}")
if len(cs_e) != len(rs_e) or len(cs_e) % 8 != 0:
    print("easing: SIZE MISMATCH")
    sys.exit(1)

n = len(cs_e) // 8
exact = 0
ulp_hist = {}
max_ulp = 0
max_abs = 0.0
over_1e15 = 0
names = (
    [f"named[{i}]" for i in range(31)]
    + ["CubicBezier(0.25,0.1,0.25,1.0)", "CubicBezier(0.42,0.0,0.58,1.0)", "CubicBezier(0.68,-0.55,0.265,1.55)"]
)
per_easing_max_ulp = [0] * 34
per_easing_mismatches = [0] * 34

for i in range(n):
    c = struct.unpack_from("<d", cs_e, i * 8)[0]
    r = struct.unpack_from("<d", rs_e, i * 8)[0]
    cb = struct.unpack_from("<Q", cs_e, i * 8)[0]
    rb = struct.unpack_from("<Q", rs_e, i * 8)[0]
    ulp = abs(cb - rb)
    ad = abs(c - r)
    eidx = i // 1001
    if ulp == 0:
        exact += 1
    else:
        ulp_hist[ulp] = ulp_hist.get(ulp, 0) + 1
        per_easing_mismatches[eidx] += 1
    if ulp > max_ulp:
        max_ulp = ulp
    if ad > max_abs:
        max_abs = ad
    if ad > 1e-15:
        over_1e15 += 1
    if ulp > per_easing_max_ulp[eidx]:
        per_easing_max_ulp[eidx] = ulp

print(f"easing samples: {n}")
print(f"easing exact-bit matches: {exact}/{n}")
print(f"easing bit-mismatches: {n - exact}")
print(f"easing max ulp: {max_ulp}")
print(f"easing max abs: {max_abs:.3e}")
print(f"easing abs > 1e-15: {over_1e15}")
print(f"easing ulp histogram: {dict(sorted(ulp_hist.items()))}")
for i, name in enumerate(names):
    if per_easing_mismatches[i] or per_easing_max_ulp[i]:
        print(f"  {name}: mismatches={per_easing_mismatches[i]} max_ulp={per_easing_max_ulp[i]}")

cs_g = (tmp / "cs_geometry.txt").read_text().splitlines()
rs_g = (tmp / "rs_geometry.txt").read_text().splitlines()
print(f"geometry lines csharp={len(cs_g)} rust={len(rs_g)}")
if len(cs_g) != len(rs_g):
    print("geometry: LINE COUNT MISMATCH")
    sys.exit(1)

def is_float_line(line: str) -> bool:
    return line.startswith(("bezier_len ", "line_len ", "norm_dist "))

def fbits_to_float(hexs: str) -> float:
    return struct.unpack("<d", bytes.fromhex(hexs))[0]

coord_exact = 0
coord_mismatch = 0
float_exact = 0
float_mismatch = 0
float_max_ulp = 0
float_max_abs = 0.0
float_over_1e15 = 0
coord_mismatch_examples = []
float_mismatch_examples = []

for e, a in zip(rs_g, cs_g):
    if is_float_line(e):
        if e == a:
            float_exact += 1
            continue
        float_mismatch += 1
        try:
            eh = e.rsplit(": ", 1)[1]
            ah = a.rsplit(": ", 1)[1]
            ev = fbits_to_float(eh)
            av = fbits_to_float(ah)
            eb = int.from_bytes(bytes.fromhex(eh), "little")
            ab = int.from_bytes(bytes.fromhex(ah), "little")
            ulp = abs(eb - ab)
            ad = abs(ev - av)
            if ulp > float_max_ulp:
                float_max_ulp = ulp
            if ad > float_max_abs:
                float_max_abs = ad
            if ad > 1e-15:
                float_over_1e15 += 1
            if len(float_mismatch_examples) < 5:
                float_mismatch_examples.append((e, a, ulp, ad))
        except Exception as ex:
            if len(float_mismatch_examples) < 5:
                float_mismatch_examples.append((e, a, "parse", ex))
    else:
        if e == a:
            coord_exact += 1
        else:
            coord_mismatch += 1
            if len(coord_mismatch_examples) < 5:
                coord_mismatch_examples.append((e, a))

print(f"geometry quantized (coord) lines exact: {coord_exact}/{coord_exact + coord_mismatch}")
print(f"geometry quantized mismatches: {coord_mismatch}")
print(f"geometry raw-float lines exact: {float_exact}/{float_exact + float_mismatch}")
print(f"geometry raw-float mismatches: {float_mismatch}")
print(f"geometry float max ulp: {float_max_ulp}")
print(f"geometry float max abs: {float_max_abs:.3e}")
print(f"geometry float abs > 1e-15: {float_over_1e15}")
for pair in coord_mismatch_examples:
    print(f"  COORD DIFF rust={pair[0]!r}")
    print(f"             csharp={pair[1]!r}")
for row in float_mismatch_examples:
    print(f"  FLOAT DIFF rust={row[0]!r}")
    print(f"             csharp={row[1]!r} ulp={row[2]} abs={row[3]}")

if coord_mismatch == 0:
    print("VERDICT: quantized integer lattice MATCHES — byte-exact CI can run on macOS")
else:
    print("VERDICT: quantized integer lattice DIFFERS — keep Linux-only gate")
PY
