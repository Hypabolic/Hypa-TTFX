#!/usr/bin/env bash
# Blackhole parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" blackhole "$@"
