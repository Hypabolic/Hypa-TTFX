#!/usr/bin/env bash
# Beams parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" beams "$@"
