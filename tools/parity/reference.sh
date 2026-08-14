#!/usr/bin/env bash
# Sourceable adapter for the Rust parity oracle.
#
# Functions resolve the binary at $ROOT/reference/ttfx only — never via PATH.
# `command -v ttfx` is unrelated and must stay that way.
#
#   source tools/parity/reference.sh
#   printf 'hi\n' | ref_dump --seed 1 wipe
#   printf 'hi\n' | ref_m0
#   printf 'hi\n' | ref_tty --seed 1 wipe
#
# Or invoke a function by name:
#   tools/parity/reference.sh ref_dump --seed 1 wipe

# Resolve this file's path when executed or sourced, from bash or zsh.
# ${(%):-%x} is zsh-only and is eval'd so bash does not parse it.
if [ -n "${BASH_SOURCE[0]:-}" ]; then
  _HYPA_REF_SRC="${BASH_SOURCE[0]}"
elif [ -n "${ZSH_VERSION:-}" ]; then
  eval '_HYPA_REF_SRC="${(%):-%x}"'
else
  _HYPA_REF_SRC="$0"
fi

ROOT="$(CDPATH= cd -- "${_HYPA_REF_SRC%/*}/../.." && pwd)"
REF="$ROOT/reference/ttfx"
_HYPA_PTY_LAUNCH="$ROOT/tools/parity/pty_launch.py"
unset _HYPA_REF_SRC

_hypa_ref_require() {
  if [ ! -x "$REF" ]; then
    echo "reference.sh: missing executable $REF (run tools/parity/fetch_reference.sh)" >&2
    return 1
  fi
}

# Length-prefixed frames on stdout; frames=N on stderr.
# --parity-dump is required: without it the binary emits tty bytes.
ref_dump() {
  _hypa_ref_require || return 1
  "$REF" --parity-dump "$@"
}

# Single preprocessed frame on stdout.
ref_m0() {
  _hypa_ref_require || return 1
  "$REF" --m0-dump "$@"
}

# Full tty lifecycle under a real pty (isatty is true).
# --max-frames is dump-only; the tty path ignores it. Use a fast effect.
ref_tty() {
  _hypa_ref_require || return 1
  python3 "$_HYPA_PTY_LAUNCH" "$REF" --frame-rate 0 --virtual-clock "$@"
}

# Dispatch only when this file is executed, not sourced.
_HYPA_REF_EXECUTED=0
if [ -n "${BASH_SOURCE[0]:-}" ]; then
  if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    _HYPA_REF_EXECUTED=1
  fi
elif [ -n "${ZSH_VERSION:-}" ]; then
  case "${ZSH_EVAL_CONTEXT:-}" in
    *":file"*|*":source"*) _HYPA_REF_EXECUTED=0 ;;
    *) _HYPA_REF_EXECUTED=1 ;;
  esac
else
  _HYPA_REF_EXECUTED=1
fi

if [ "$_HYPA_REF_EXECUTED" -eq 1 ]; then
  unset _HYPA_REF_EXECUTED
  set -euo pipefail
  if [ "$#" -lt 1 ]; then
    echo "usage: reference.sh {ref_dump|ref_m0|ref_tty} [args...]" >&2
    exit 2
  fi
  cmd="$1"
  shift
  case "$cmd" in
    ref_dump) ref_dump "$@" ;;
    ref_m0) ref_m0 "$@" ;;
    ref_tty) ref_tty "$@" ;;
    *)
      echo "usage: reference.sh {ref_dump|ref_m0|ref_tty} [args...]" >&2
      exit 2
      ;;
  esac
else
  unset _HYPA_REF_EXECUTED
fi
