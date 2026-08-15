#!/usr/bin/env bash
# Spotlights parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" spotlights "$@"
