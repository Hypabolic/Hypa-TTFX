#!/usr/bin/env bash
# LaserEtch parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" laseretch "$@"
