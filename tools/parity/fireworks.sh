#!/usr/bin/env bash
# Fireworks parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" fireworks "$@"
