#!/usr/bin/env bash
# Fail with the specific missing tool name rather than a linker error mid-publish.
set -euo pipefail

ROOT="$(CDPATH= cd -- "${BASH_SOURCE[0]%/*}/.." && pwd)"
cd "$ROOT"

need_parity=0
if [ "${1:-}" = "--parity" ]; then
  need_parity=1
fi

missing() {
  echo "missing required tool: $1" >&2
  exit 1
}

if ! command -v bash >/dev/null 2>&1; then
  missing "bash"
fi

if ! command -v python3 >/dev/null 2>&1; then
  missing "python3"
fi

if ! python3 -c "import pty" >/dev/null 2>&1; then
  missing "pty"
fi

if ! command -v clang >/dev/null 2>&1; then
  missing "clang"
fi

if ! command -v ld >/dev/null 2>&1; then
  missing "ld"
fi

if ! command -v dotnet >/dev/null 2>&1; then
  missing "dotnet"
fi

required_sdk="$(python3 -c "import json; print(json.load(open('${ROOT}/global.json'))['sdk']['version'])")"
if ! dotnet --version >/dev/null 2>&1; then
  missing ".NET SDK ${required_sdk}"
fi

rid="$(dotnet --info | awk -F': *' '/^[[:space:]]*RID:/{gsub(/[[:space:]]/, "", $2); print $2; exit}')"
if [ -z "$rid" ]; then
  missing "host RID (dotnet --info)"
fi

pack="Microsoft.NETCore.App.Runtime.NativeAOT.${rid}"

pack_installed() {
  local name="$1"
  local base_path packs_dir nuget_root nuget_name
  base_path="$(dotnet --info | awk -F': *' '/^[[:space:]]*Base Path:/{print $2; exit}')"
  if [ -n "$base_path" ]; then
    packs_dir="$(cd "${base_path}/../../packs" 2>/dev/null && pwd || true)"
    if [ -n "${packs_dir:-}" ] && [ -d "${packs_dir}/${name}" ]; then
      return 0
    fi
  fi
  nuget_root="$(dotnet nuget locals global-packages --list 2>/dev/null | awk -F': ' '{print $2; exit}' || true)"
  nuget_name="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"
  if [ -n "${nuget_root:-}" ] && [ -d "${nuget_root}/${nuget_name}" ]; then
    return 0
  fi
  return 1
}

# Desktop SDK installers ship this pack under packs/. actions/setup-dotnet does
# not; download the matching runtime pack into the NuGet cache so publish works
# on a clean CI image without adding a PackageReference to the product.
if ! pack_installed "$pack"; then
  runtime_ver="$(dotnet --list-runtimes | awk '/^Microsoft.NETCore.App 10\./ { print $2 }' | sort -V | tail -1)"
  if [ -z "${runtime_ver:-}" ]; then
    missing ".NET 10 runtime (dotnet --list-runtimes)"
  fi
  tmp="$(mktemp -d)"
  # Keep this project outside the repo so Directory.Build.props does not apply.
  cat > "$tmp/aot-pack.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageDownload Include="${pack}" Version="[${runtime_ver}]" />
  </ItemGroup>
</Project>
EOF
  echo "check-prereqs: downloading ${pack} ${runtime_ver}" >&2
  if ! dotnet restore "$tmp/aot-pack.csproj" --nologo; then
    rm -rf "$tmp"
    missing "$pack"
  fi
  rm -rf "$tmp"
  if ! pack_installed "$pack"; then
    missing "$pack"
  fi
fi

# StripSymbols=true: Linux ILC needs objcopy/llvm-objcopy. Apple ILC uses
# dsymutil + strip (see Microsoft.NETCore.Native.Unix.targets).
if grep -q '<StripSymbols>true</StripSymbols>' "$ROOT/src/Ttfx/Ttfx.csproj"; then
  if [ "$(uname -s)" = "Darwin" ]; then
    if ! command -v dsymutil >/dev/null 2>&1; then
      missing "dsymutil"
    fi
    if ! command -v strip >/dev/null 2>&1; then
      missing "strip"
    fi
  else
    if ! command -v objcopy >/dev/null 2>&1 && ! command -v llvm-objcopy >/dev/null 2>&1; then
      missing "objcopy"
    fi
  fi
fi

if [ "$need_parity" -eq 1 ]; then
  if ! command -v cargo >/dev/null 2>&1; then
    missing "cargo"
  fi
  if ! command -v rustc >/dev/null 2>&1; then
    missing "rustc"
  fi
  if ! command -v git >/dev/null 2>&1; then
    missing "git"
  fi
fi

if ! command -v zsh >/dev/null 2>&1; then
  echo "notice: zsh not found; the completion check will be skipped" >&2
fi
