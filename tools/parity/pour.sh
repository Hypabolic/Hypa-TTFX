#!/usr/bin/env bash
# Pour parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" pour "$@"
