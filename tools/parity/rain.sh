#!/usr/bin/env bash
# Rain parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" rain "$@"
