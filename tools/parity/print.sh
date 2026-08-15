#!/usr/bin/env bash
# Print parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" print "$@"
