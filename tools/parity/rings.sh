#!/usr/bin/env bash
# Rings parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" rings "$@"
