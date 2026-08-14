#!/usr/bin/env bash
# Middleout parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" middleout "$@"
