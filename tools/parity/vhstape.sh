#!/usr/bin/env bash
# VhsTape parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" vhstape "$@"
