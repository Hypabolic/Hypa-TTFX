#!/usr/bin/env bash
# OrbittingVolley parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" orbittingvolley "$@"
