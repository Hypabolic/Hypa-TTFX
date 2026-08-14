#!/usr/bin/env bash
# Expand parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" expand "$@"
