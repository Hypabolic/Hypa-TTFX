#!/usr/bin/env bash
# Binarypath parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" binarypath "$@"
