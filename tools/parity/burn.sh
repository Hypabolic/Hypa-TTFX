#!/usr/bin/env bash
# Burn parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" burn "$@"
