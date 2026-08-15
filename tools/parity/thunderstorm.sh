#!/usr/bin/env bash
# Thunderstorm parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" thunderstorm "$@"
