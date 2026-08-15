#!/usr/bin/env bash
# Swarm parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" swarm "$@"
